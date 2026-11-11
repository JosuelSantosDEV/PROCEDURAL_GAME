using System;
using PROCEDURAL_GAME.World;
using UnityEngine;

namespace PROCEDURAL_GAME.Player
{
    /// <summary>
    /// Ações que prendem o corpo enquanto a animação toca: atacar (sequência gancho, chute e direto no mesmo botão), coletar
    /// (agachar, pegar, levantar: beber, comer e, depois, lanterna, chave...) e morrer. O efeito da coleta acontece no ponto mais baixo do agachar.
    /// O tempo vem da duração dos clipes, gravada pelo menu Setup Player Model; o corpo é sempre solto por relógio, nunca por evento de animação.
    /// Cada golpe tem três fases: preparação e impacto (compromisso: não cancela) e recuperação (a partir do fim do impacto, medido no
    /// clipe pelo menu). Na recuperação, pular ou inclinar a manete encerra o golpe na hora; um toque de pular dado antes fica guardado e
    /// sai assim que a recuperação começa.
    /// Sem golpes acertando nada ainda: os inimigos não existem.
    /// </summary>
    // Depois do PlayerMovement (100) e do PlayerStats (105).
    [DefaultExecutionOrder(106)]
    [RequireComponent(typeof(PlayerMovement), typeof(PlayerStats), typeof(PlayerInputReader))]
    public class PlayerActions : MonoBehaviour
    {
        /// <summary>O que o botão de interação faz agora.</summary>
        public enum Interaction
        {
            None,
            Drink,
            Eat
        }

        private static readonly int HookId = Animator.StringToHash("Hook");
        private static readonly int KickId = Animator.StringToHash("Kick");
        private static readonly int CrossId = Animator.StringToHash("Cross");
        private static readonly int CollectId = Animator.StringToHash("Collect");
        private static readonly int DieId = Animator.StringToHash("Die");
        private static readonly int AttackingId = Animator.StringToHash("Attacking");

        // Inclinação da manete (0 a 1) a partir da qual ela encerra a recuperação de um golpe.
        private const float MoveCancelThreshold = 0.2f;

        // Quanto antes do fim do golpe o próximo golpe guardado entra (s): igual ao fade entre eles no controller (PlayerBuilder.AttackChainFade).
        private const float AttackChainFade = 0.15f;

        [Tooltip("Animator do modelo. Se vazio, procura nos filhos.")]
        [SerializeField] private Animator animator;

        [Tooltip("Energia gasta (0 a 1) por ataque. Sem energia não ataca.")]
        [SerializeField, Range(0f, 1f)] private float attackEnergyCost = 0.15f;

        [Tooltip("Distância (m) em que já dá para pegar um objeto do chão.")]
        [SerializeField, Min(0f)] private float pickupReach = 3f;

        [Tooltip("Depois do golpe, tempo (s) em que o corpo ainda fica parado esperando outro toque de ataque. Com a manete inclinada e toques seguidos, é o que evita andar entre um golpe e outro.")]
        [SerializeField, Min(0f)] private float attackRecovery = 0.2f;

        [Tooltip("Sem atacar por este tempo (s), a sequência recomeça pelo gancho.")]
        [SerializeField, Min(0f)] private float comboResetSeconds = 1.5f;

        [Header("Duração das animações (s), gravada pelo menu Setup Player Model")]
        [SerializeField, Min(0.1f)] private float hookSeconds = 0.9f;

        [SerializeField, Min(0.1f)] private float kickSeconds = 1.1f;

        [SerializeField, Min(0.1f)] private float crossSeconds = 0.9f;

        [SerializeField, Min(0.1f)] private float crouchDownSeconds = 1f;

        [SerializeField, Min(0.1f)] private float crouchUpSeconds = 1f;

        [SerializeField, Min(0.1f)] private float deathSeconds = 2.1f;

        [Header("Janela do impacto de cada golpe (0 a 1 do clipe), medida pelo menu Setup Player Model")]
        [Tooltip("Começo e fim do movimento rápido do golpe (mão ou pé). Depois do fim começa a recuperação, que pode ser cancelada. Servirá também para o golpe acertar os inimigos.")]
        [SerializeField] private Vector2 hookHitWindow = new Vector2(0.3f, 0.5f);

        [SerializeField] private Vector2 kickHitWindow = new Vector2(0.386f, 0.499f);

        [SerializeField] private Vector2 crossHitWindow = new Vector2(0.3f, 0.5f);

        private PlayerMovement movement;
        private PlayerStats stats;
        private PlayerInputReader input;
        private WorldGenerator world;
        private PickupRegistry.Item nearbyPickup;
        private PickupRegistry.Item pendingPickup;
        private Interaction pendingEffect;
        private float effectAt;
        private float lockedUntil;
        private int comboIndex;
        private bool attacking;
        private bool attackQueued;
        private bool jumpQueued;
        private float attackEndsAt = -1000f;
        private float recoveryAt;
        private bool hasAttackingParameter;
        private bool dead;

        /// <summary>O que dá para fazer aqui. O botão só aparece com algo disponível.</summary>
        public Interaction Available { get; private set; }

        /// <summary>Quanto dura a animação de morte (a tela de morte espera).</summary>
        public float DeathSeconds => deathSeconds;

        /// <summary>Texto curto para o jogador ("Sem energia", "Você comeu"...).</summary>
        public event Action<string> Message;

        private void Awake()
        {
            movement = GetComponent<PlayerMovement>();
            stats = GetComponent<PlayerStats>();
            input = GetComponent<PlayerInputReader>();
            if (animator == null) animator = GetComponentInChildren<Animator>();

            // Controller antigo (antes de rodar o menu de novo) não tem o parâmetro: não o escreve (evita avisos).
            hasAttackingParameter = animator != null && PlayerAnimator.HasParameter(animator, AttackingId);
        }

        private void Start()
        {
            world = FindObjectOfType<WorldGenerator>();
        }

        private void OnEnable()
        {
            stats.Died += OnDied;
        }

        private void OnDisable()
        {
            stats.Died -= OnDied;
        }

        private void Update()
        {
            if (dead) return;

            if (movement.ActionLocked)
            {
                if (pendingEffect != Interaction.None && Time.time >= effectAt) ApplyEffect();

                if (attacking)
                {
                    ReadAttackInput();
                    if (TryCancelAttack()) return;
                    UpdateAttackChain();
                }

                if (Time.time >= lockedUntil) Unlock();
                return;
            }

            UpdateAvailable();

            // JumpAllowed é falso no ar e durante o pouso: não ataca nesses momentos.
            if (input.AttackPressed && movement.JumpAllowed) StartAttack();
        }

        // Durante um golpe vale o ÚLTIMO pedido: atacar de novo (encadeia) ou pular (encerra o golpe). Um só fica guardado.
        private void ReadAttackInput()
        {
            if (input.JumpPressed)
            {
                jumpQueued = true;
                attackQueued = false;
            }

            if (input.AttackPressed)
            {
                attackQueued = true;
                jumpQueued = false;
            }
        }

        /// <summary>
        /// Na recuperação do golpe: pulo guardado sai na hora; manete inclinada (sem outro golpe guardado) volta a andar. A transição
        /// do Animator começa no mesmo frame (o pulo pelo gatilho do PlayerMovement no frame seguinte, junto com a física).
        /// </summary>
        private bool TryCancelAttack()
        {
            if (Time.time < recoveryAt) return false;

            if (jumpQueued)
            {
                jumpQueued = false;
                if (!movement.IsGrounded) return false;

                Unlock();
                movement.RequestJump();
                return true;
            }

            if (!attackQueued && input.Move.sqrMagnitude > MoveCancelThreshold * MoveCancelThreshold)
            {
                Unlock();
                return true;
            }

            return false;
        }

        private void Unlock()
        {
            movement.ActionLocked = false;
            movement.TurnWhileLocked = false;
            attacking = false;
            attackQueued = false;
            jumpQueued = false;
            SetAttacking(false);
        }

        private void SetAttacking(bool value)
        {
            if (hasAttackingParameter) animator.SetBool(AttackingId, value);
        }

        // Toque durante um golpe fica guardado (um só, por mais toques que venham) e o próximo golpe entra no fim deste, sem o corpo sair do lugar.
        private void UpdateAttackChain()
        {
            if (attackQueued && Time.time >= attackEndsAt - AttackChainFade)
            {
                attackQueued = false;
                StartAttack();
            }
        }

        /// <summary>Chamado pelo botão de interação: agacha, faz o efeito no ponto mais baixo e levanta.</summary>
        public void Interact()
        {
            if (dead || movement.ActionLocked || !movement.JumpAllowed || Available == Interaction.None) return;

            pendingEffect = Available;
            pendingPickup = nearbyPickup;
            effectAt = Time.time + crouchDownSeconds;

            Lock(crouchDownSeconds + crouchUpSeconds, false);
            animator.SetTrigger(CollectId);
        }

        private void StartAttack()
        {
            if (!stats.TrySpendEnergy(attackEnergyCost))
            {
                Message?.Invoke("Sem energia");
                return;
            }

            if (Time.time > attackEndsAt + comboResetSeconds) comboIndex = 0;

            float seconds;
            Vector2 window;
            switch (comboIndex)
            {
                case 0:
                    seconds = hookSeconds;
                    window = hookHitWindow;
                    animator.SetTrigger(HookId);
                    break;
                case 1:
                    seconds = kickSeconds;
                    window = kickHitWindow;
                    animator.SetTrigger(KickId);
                    break;
                default:
                    seconds = crossSeconds;
                    window = crossHitWindow;
                    animator.SetTrigger(CrossId);
                    break;
            }

            comboIndex = (comboIndex + 1) % 3;
            attacking = true;
            jumpQueued = false;
            attackEndsAt = Time.time + seconds;
            recoveryAt = Time.time + seconds * Mathf.Clamp01(window.y);
            SetAttacking(true);
            // Durante o golpe (e a espera depois dele) o corpo pode virar, para mudar a direção do ataque; só não anda.
            Lock(seconds + attackRecovery, true);
        }

        private void Lock(float seconds, bool canTurn)
        {
            movement.ActionLocked = true;
            movement.TurnWhileLocked = canTurn;
            lockedUntil = Time.time + seconds;
            Available = Interaction.None;
        }

        // Pegar algo do chão tem prioridade sobre beber: quem está na margem ao lado de uma fruta quer a fruta.
        private void UpdateAvailable()
        {
            PickupRegistry pickups = world != null ? world.Pickups : null;

            // Nadando não dá para agachar: nada de beber ou pegar.
            if (movement.IsSwimming) Available = Interaction.None;
            else if (pickups != null && pickups.TryFindNearest(transform.position, pickupReach, out nearbyPickup)) Available = Interaction.Eat;
            else Available = stats.CanDrink ? Interaction.Drink : Interaction.None;
        }

        private void ApplyEffect()
        {
            if (pendingEffect == Interaction.Drink)
            {
                stats.Drink();
                Message?.Invoke("Você bebeu água");
            }
            else if (pendingEffect == Interaction.Eat)
            {
                world.Pickups.Collect(pendingPickup);
                stats.Eat();
                Message?.Invoke("Você comeu");
            }

            pendingEffect = Interaction.None;
            pendingPickup = null;
        }

        private void OnDied()
        {
            dead = true;
            Available = Interaction.None;
            movement.ActionLocked = true;
            movement.TurnWhileLocked = false;
            attacking = false;
            SetAttacking(false);

            // O ajuste dos pés ao chão puxaria as pernas do corpo caído.
            PlayerFootIK footIk = animator.GetComponent<PlayerFootIK>();
            if (footIk != null) footIk.enabled = false;

            animator.SetTrigger(DieId);
        }
    }
}
