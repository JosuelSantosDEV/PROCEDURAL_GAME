using System;
using PROCEDURAL_GAME.World;
using UnityEngine;

namespace PROCEDURAL_GAME.Player
{
    /// <summary>
    /// Ações que prendem o corpo enquanto a animação toca: atacar (sequência gancho, chute e direto no mesmo botão), coletar
    /// (agachar, pegar, levantar: beber, comer e, depois, lanterna, chave...) e morrer. O efeito da coleta acontece no ponto mais baixo do agachar.
    /// O tempo vem da duração dos clipes, gravada pelo menu Setup Player Model; o corpo é sempre solto por relógio, nunca por evento de animação.
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
        private float attackEndsAt = -1000f;
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
                if (attacking) UpdateAttackChain();

                if (Time.time >= lockedUntil)
                {
                    movement.ActionLocked = false;
                    attacking = false;
                    attackQueued = false;
                }

                return;
            }

            UpdateAvailable();

            // JumpAllowed é falso no ar e durante o pouso: não ataca nesses momentos.
            if (input.AttackPressed && movement.JumpAllowed) StartAttack();
        }

        // Toque durante um golpe fica guardado (um só, por mais toques que venham) e o próximo golpe entra no fim deste, sem o corpo sair do lugar.
        private void UpdateAttackChain()
        {
            if (input.AttackPressed) attackQueued = true;

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

            Lock(crouchDownSeconds + crouchUpSeconds);
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
            switch (comboIndex)
            {
                case 0:
                    seconds = hookSeconds;
                    animator.SetTrigger(HookId);
                    break;
                case 1:
                    seconds = kickSeconds;
                    animator.SetTrigger(KickId);
                    break;
                default:
                    seconds = crossSeconds;
                    animator.SetTrigger(CrossId);
                    break;
            }

            comboIndex = (comboIndex + 1) % 3;
            attacking = true;
            attackEndsAt = Time.time + seconds;
            Lock(seconds + attackRecovery);
        }

        private void Lock(float seconds)
        {
            movement.ActionLocked = true;
            lockedUntil = Time.time + seconds;
            Available = Interaction.None;
        }

        // Pegar algo do chão tem prioridade sobre beber: quem está na margem ao lado de uma fruta quer a fruta.
        private void UpdateAvailable()
        {
            PickupRegistry pickups = world != null ? world.Pickups : null;

            if (pickups != null && pickups.TryFindNearest(transform.position, pickupReach, out nearbyPickup)) Available = Interaction.Eat;
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

            // O ajuste dos pés ao chão puxaria as pernas do corpo caído.
            PlayerFootIK footIk = animator.GetComponent<PlayerFootIK>();
            if (footIk != null) footIk.enabled = false;

            animator.SetTrigger(DieId);
        }
    }
}
