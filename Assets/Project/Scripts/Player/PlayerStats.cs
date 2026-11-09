using System;
using PROCEDURAL_GAME.World;
using UnityEngine;

namespace PROCEDURAL_GAME.Player
{
    /// <summary>
    /// Sobrevivência do Player: duas barras (vida e energia) e dois relógios escondidos (sede e fome).
    /// Sem beber ou comer por um tempo aparece o ícone; pouco depois a vida começa a cair até o Player resolver (sede e fome juntas somam).
    /// Com ícone ativo a vida não se recupera sozinha. Beber ou comer zera o relógio e, só se o ícone estava ativo, devolve um bônus de vida e
    /// energia (sem isso, ficar na margem de um lago bebendo seria uma cura infinita).
    /// Energia: correr e atacar gastam; ao zerar, nenhum dos dois é possível até recuperar um pouco.
    /// Não desenha nada: a tela fica em UI/SurvivalHud.
    /// </summary>
    // Depois do PlayerMovement (100): usa o movimento deste frame.
    [DefaultExecutionOrder(105)]
    [RequireComponent(typeof(PlayerMovement), typeof(PlayerInputReader))]
    public class PlayerStats : MonoBehaviour
    {
        [Header("Vida")]
        [Tooltip("Tempo (s) para recuperar a vida inteira sozinha, depois de um ataque.")]
        [SerializeField, Min(1f)] private float lifeRegenSeconds = 120f;

        [Tooltip("Espera (s) depois de um ataque antes de a vida começar a voltar.")]
        [SerializeField, Min(0f)] private float lifeRegenDelay = 5f;

        [Header("Energia")]
        [Tooltip("Tempo (s) correndo até a energia zerar.")]
        [SerializeField, Min(1f)] private float sprintSeconds = 15f;

        [Tooltip("Tempo (s) para a energia voltar inteira.")]
        [SerializeField, Min(1f)] private float energyRegenSeconds = 10f;

        [Tooltip("Espera (s) depois de parar de correr antes de a energia começar a voltar.")]
        [SerializeField, Min(0f)] private float energyRegenDelay = 1f;

        [Tooltip("Depois de zerar, quanto da energia (0 a 1) precisa voltar para poder correr de novo. Evita alternar correr e andar a cada instante.")]
        [SerializeField, Range(0f, 1f)] private float exhaustedUntil = 0.2f;

        // Tempos CURTOS DE DESENVOLVIMENTO: até 3 min da última água/comida até a morte. Ao ajustar para o jogo final, troque aqui e no Inspector
        // (o valor fica gravado na cena depois que o componente é adicionado).
        [Header("Sede (tempos de teste)")]
        [Tooltip("Segundos sem beber até o ícone aparecer.")]
        [SerializeField, Min(0f)] private float thirstIconSeconds = 45f;

        [Tooltip("Segundos sem beber até a vida começar a cair.")]
        [SerializeField, Min(0f)] private float thirstDrainStart = 60f;

        [Tooltip("Tempo (s) da vida cheia até zero só com a sede.")]
        [SerializeField, Min(1f)] private float thirstDrainSeconds = 120f;

        [Tooltip("Distância (m) da margem em que já dá para beber.")]
        [SerializeField, Min(0f)] private float drinkReach = 4f;

        [Header("Fome (tempos de teste)")]
        [SerializeField, Min(0f)] private float hungerIconSeconds = 75f;

        [SerializeField, Min(0f)] private float hungerDrainStart = 90f;

        [SerializeField, Min(1f)] private float hungerDrainSeconds = 90f;

        [Header("Beber e comer")]
        [Tooltip("Vida e energia devolvidas (0 a 1) ao matar a sede ou a fome com o ícone ativo.")]
        [SerializeField, Range(0f, 1f)] private float needBonus = 0.25f;

        private PlayerMovement movement;
        private PlayerInputReader input;
        private CharacterController body;
        private WorldGenerator world;
        private float thirstTimer;
        private float hungerTimer;
        private float lastDamageTime = -1000f;
        private float lastEnergyUse = -1000f;
        private bool exhausted;

#if UNITY_EDITOR || DEV_TOOLS
        /// <summary>Ferramenta DEV: para de contar sede e fome, para trabalhar no resto do jogo sem morrer a cada 3 min.</summary>
        public static bool DEV_NeedsPaused { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void DEV_ClearStatics() => DEV_NeedsPaused = false;
#endif

        /// <summary>Vida de 0 a 1.</summary>
        public float Life { get; private set; } = 1f;

        /// <summary>Energia de 0 a 1.</summary>
        public float Energy { get; private set; } = 1f;

        public bool IsDead { get; private set; }

        /// <summary>O ícone de sede está ativo. Só existe sede em bioma com lagos: sem água no mapa não haveria como resolver.</summary>
        public bool IsThirsty => thirstTimer >= thirstIconSeconds;

        /// <summary>O ícone de fome está ativo. Só existe fome em bioma com comida, pelo mesmo motivo.</summary>
        public bool IsHungry => hungerTimer >= hungerIconSeconds;

        /// <summary>O Player está na beira ou dentro de um lago.</summary>
        public bool CanDrink { get; private set; }

        /// <summary>Disparado uma vez, quando a vida chega a zero.</summary>
        public event Action Died;

        private void Awake()
        {
            movement = GetComponent<PlayerMovement>();
            input = GetComponent<PlayerInputReader>();
            body = GetComponent<CharacterController>();
        }

        private void Start()
        {
            world = FindObjectOfType<WorldGenerator>();
        }

        private void Update()
        {
            if (IsDead) return;

            float dt = Time.deltaTime;
            LakeMap lakes = world != null ? world.Lakes : null;

            UpdateEnergy(dt);
            CanDrink = lakes != null && IsNearWater(lakes);

#if UNITY_EDITOR || DEV_TOOLS
            bool needsPaused = DEV_NeedsPaused;
#else
            const bool needsPaused = false;
#endif
            if (!needsPaused)
            {
                if (lakes != null) thirstTimer += dt;
                if (world != null && world.HasFood) hungerTimer += dt;
            }

            float drain = 0f;
            if (thirstTimer >= thirstDrainStart) drain += 1f / thirstDrainSeconds;
            if (hungerTimer >= hungerDrainStart) drain += 1f / hungerDrainSeconds;

            if (drain > 0f)
            {
                SetLife(Life - drain * dt);
            }
            else if (!IsThirsty && !IsHungry && Time.time - lastDamageTime >= lifeRegenDelay)
            {
                SetLife(Life + dt / lifeRegenSeconds);
            }
        }

        /// <summary>Ataque recebido: tira esta fração (0 a 1) da vida e adia a recuperação.</summary>
        public void TakeDamage(float fraction)
        {
            if (IsDead) return;

            lastDamageTime = Time.time;
            SetLife(Life - fraction);
        }

        /// <summary>Bebe: zera a sede. O bônus só vem se o ícone estava ativo.</summary>
        public void Drink()
        {
            if (IsDead) return;

            if (IsThirsty) GiveBonus();
            thirstTimer = 0f;
        }

        /// <summary>Come: zera a fome. O bônus só vem se o ícone estava ativo.</summary>
        public void Eat()
        {
            if (IsDead) return;

            if (IsHungry) GiveBonus();
            hungerTimer = 0f;
        }

        /// <summary>Gasta energia de uma ação (ataque). Falso = sem energia, a ação não acontece.</summary>
        public bool TrySpendEnergy(float amount)
        {
            if (IsDead || exhausted || Energy <= 0f) return false;

            lastEnergyUse = Time.time;
            Energy = Mathf.Max(0f, Energy - amount);
            if (Energy <= 0f) Exhaust();
            return true;
        }

        private void Exhaust()
        {
            exhausted = true;
            movement.SprintBlocked = true;
            input.CancelSprint();
        }

        private void GiveBonus()
        {
            SetLife(Life + needBonus);
            Energy = Mathf.Clamp01(Energy + needBonus);
        }

        private void UpdateEnergy(float dt)
        {
            if (movement.IsSprintMoving)
            {
                lastEnergyUse = Time.time;
                Energy = Mathf.Max(0f, Energy - dt / sprintSeconds);

                if (Energy <= 0f) Exhaust();
            }
            else if (Time.time - lastEnergyUse >= energyRegenDelay)
            {
                Energy = Mathf.Min(1f, Energy + dt / energyRegenSeconds);
            }

            if (exhausted && Energy >= exhaustedUntil)
            {
                exhausted = false;
                movement.SprintBlocked = false;
            }
        }

        private bool IsNearWater(LakeMap lakes)
        {
            Vector3 position = transform.position;
            if (!lakes.Query(position.x, position.z, out float distance, out float level, out _) || distance > drinkReach) return false;

            float feet = body != null ? body.bounds.min.y : position.y;
            return feet < level + drinkReach;
        }

        private void SetLife(float value)
        {
            Life = Mathf.Clamp01(value);
            if (Life > 0f || IsDead) return;

            IsDead = true;
            Died?.Invoke();
        }
    }
}
