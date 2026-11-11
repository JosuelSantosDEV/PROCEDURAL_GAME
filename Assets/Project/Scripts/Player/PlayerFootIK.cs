using UnityEngine;

namespace PROCEDURAL_GAME.Player
{
    /// <summary>
    /// Ajusta os pés ao relevo (IK de pés). As animações assumem chão plano: em uma ladeira o pé de cima ficava "voando" e o de
    /// baixo afundava. A cada frame lança um raio sob cada pé, desloca o pé na vertical pela diferença entre a altura do chão ali e a
    /// altura do pivô do modelo, inclina a sola pela normal do chão e abaixa o corpo todo até o pé mais baixo alcançar o chão.
    /// Todas as distâncias abaixo (metros) valem para o modelo em escala 1 e são multiplicadas pela escala do objeto (o Player é 2,5x).
    /// Só atua com o pé apoiado (um pé levantado na corrida/pulo fica livre) e com o Player no chão.
    /// Vai no objeto do Animator ("Model"; o callback OnAnimatorIK só chega aí) e exige "IK Pass" ligado na camada 0 do controller
    /// (PROCEDURAL_GAME > Player > Setup Player Model faz os dois). Custo: 2 raios por frame.
    /// </summary>
    // Depois do PlayerAnimator (110): usa a correção de altura (VisualLift) calculada neste mesmo frame.
    [DefaultExecutionOrder(120)]
    [RequireComponent(typeof(Animator))]
    public class PlayerFootIK : MonoBehaviour
    {
        private const int HitBufferSize = 8;
        private const float MinWalkableNormalY = 0.5f;

        private static readonly int GroundedId = Animator.StringToHash("Grounded");

        [Header("Chão")]
        [Tooltip("Camadas que contam como chão para os pés.")]
        [SerializeField] private LayerMask groundMask = ~0;

        [Tooltip("Altura do raio acima do pé (m). Cobre o degrau para cima.")]
        [SerializeField, Min(0.1f)] private float rayStart = 0.5f;

        [Tooltip("Comprimento do raio a partir do ponto inicial (m). Cobre o degrau para baixo.")]
        [SerializeField, Min(0.3f)] private float rayLength = 1.3f;

        [Header("Pés")]
        [Tooltip("Altura do tornozelo acima da sola (m), usada para saber se o pé está apoiado. 0 = mede no modelo ao iniciar.")]
        [SerializeField, Min(0f)] private float footHeight;

        [Tooltip("Altura (m) do pé acima do apoio em que o IK já está pela metade (o pé levantado na corrida fica livre).")]
        [SerializeField, Min(0.02f)] private float liftFade = 0.2f;

        [Tooltip("Inclinação máxima da sola em graus.")]
        [SerializeField, Range(0f, 60f)] private float maxFootTilt = 35f;

        [Tooltip("Suavização do chão sob o pé (por segundo). Menor = mais macio, maior = mais fiel.")]
        [SerializeField, Min(1f)] private float groundFollow = 20f;

        [Header("Corpo")]
        [Tooltip("Quanto o corpo pode descer (m) para o pé de baixo alcançar o chão.")]
        [SerializeField, Min(0f)] private float maxPelvisDrop = 0.45f;

        [Tooltip("Suavização da descida/subida do corpo (por segundo).")]
        [SerializeField, Min(1f)] private float pelvisFollow = 8f;

        [Tooltip("Velocidade (por segundo) com que o IK liga e desliga ao pousar/pular.")]
        [SerializeField, Min(1f)] private float fadeSpeed = 8f;

        [Tooltip("Velocidade (por segundo) com que o IK religa ao pousar. Mais lenta que a de desligar: os pés não são puxados de uma vez para o chão durante o fade do pouso.")]
        [SerializeField, Min(1f)] private float landFadeSpeed = 4f;

        private sealed class Foot
        {
            public AvatarIKGoal goal;
            public Transform bone;
            public float groundY;
            public Vector3 normal = Vector3.up;
            public bool hasGround;
            public bool initialized;
        }

        private readonly RaycastHit[] hits = new RaycastHit[HitBufferSize];
        private readonly Foot left = new Foot { goal = AvatarIKGoal.LeftFoot };
        private readonly Foot right = new Foot { goal = AvatarIKGoal.RightFoot };

        private Animator animator;
        private PlayerMovement movement;
        private Transform owner;
        private Vector3 baseLocalPosition;
        private float scale = 1f;
        private float footWorldHeight;
        private float ikWeight;
        private float pelvisTarget;
        private float pelvisOffset;

        /// <summary>
        /// Correção vertical extra do corpo (m), pedida pelo PlayerAnimator enquanto a pose de queda do Falling To Roll aparece: o clipe
        /// começa com o corpo bem mais alto que o ápice do pulo, e sem isso o corpo daria um salto para cima ao trocar de pose.
        /// </summary>
        public float VisualLift { get; set; }

        /// <summary>Inclinação do corpo para a frente (graus; positivo = cabeça para baixo), pedida pelo PlayerAnimator ao nadar mergulhado.</summary>
        public float VisualPitch { get; set; }

        /// <summary>Altura (m, acima dos pés) do ponto em torno do qual o corpo inclina (o quadril da pose atual).</summary>
        public float PitchPivotHeight { get; set; }
        private float airUntil;
        private bool ready;

        private void Awake()
        {
            animator = GetComponent<Animator>();
            movement = GetComponentInParent<PlayerMovement>();
            owner = movement != null ? movement.transform : transform.root;

            if (animator.avatar == null || !animator.isHuman)
            {
                Debug.LogWarning($"{nameof(PlayerFootIK)}: o modelo precisa ser Humanoid. IK de pés desligado.", this);
                enabled = false;
                return;
            }

            left.bone = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            right.bone = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            if (left.bone == null || right.bone == null)
            {
                Debug.LogWarning($"{nameof(PlayerFootIK)}: ossos dos pés não encontrados. IK de pés desligado.", this);
                enabled = false;
                return;
            }

            baseLocalPosition = transform.localPosition;
            scale = Mathf.Max(0.01f, transform.lossyScale.y);

            // Na pose inicial (antes de qualquer animação) os pés estão no chão: a altura do tornozelo é a altura do apoio.
            footWorldHeight = footHeight * scale;
            if (footHeight <= 0f)
            {
                float measured = ((left.bone.position.y + right.bone.position.y) * 0.5f - transform.position.y) / scale;
                footWorldHeight = (measured > 0.02f && measured < 0.25f ? measured : 0.09f) * scale;
            }

            ready = true;
        }

        private void OnEnable()
        {
            if (movement != null) movement.Jumped += OnJumped;
        }

        private void OnDisable()
        {
            if (movement != null) movement.Jumped -= OnJumped;

            // Devolve o corpo à posição original (o objeto pode ser desligado com o corpo abaixado).
            if (ready)
            {
                pelvisOffset = 0f;
                ikWeight = 0f;
                VisualLift = 0f;
                VisualPitch = 0f;
                transform.localPosition = baseLocalPosition;
                transform.localRotation = Quaternion.identity;
            }
        }

        // Os primeiros instantes do pulo ainda contam como "no chão" para o Animator (tolerância contra falsos positivos).
        private void OnJumped()
        {
            airUntil = Time.time + 0.3f;
        }

        // O corpo desce/sobe aqui, antes do Animator avaliar a pose deste frame.
        private void Update()
        {
            if (!ready) return;

            float target = Mathf.Clamp(pelvisTarget, -maxPelvisDrop * scale, 0f) * ikWeight;
            float k = 1f - Mathf.Exp(-pelvisFollow * Time.deltaTime);
            pelvisOffset = Mathf.Lerp(pelvisOffset, target, k);
            // Inclinação em torno do quadril: a origem do modelo (nos pés) gira em volta desse ponto.
            Quaternion tilt = Quaternion.Euler(VisualPitch, 0f, 0f);
            Vector3 pivot = Vector3.up * PitchPivotHeight;
            transform.localRotation = tilt;
            transform.localPosition = baseLocalPosition + Vector3.up * (pelvisOffset + VisualLift) + (pivot - tilt * pivot);
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if (!ready || layerIndex != 0) return;

            float dt = Time.deltaTime;
            // Rolando ou nadando: prender os pés ao chão puxaria as pernas.
            bool rolling = movement != null && (movement.IsRolling || movement.IsSwimming);
            bool wantIk = animator.GetBool(GroundedId) && Time.time >= airUntil && !rolling;
            ikWeight = Mathf.MoveTowards(ikWeight, wantIk ? 1f : 0f, (wantIk ? landFadeSpeed : fadeSpeed) * dt);

            // Pivô do modelo: onde as animações supõem que o chão está (com o corpo já abaixado) e onde ele estaria sem o ajuste.
            float originY = transform.position.y;
            float baseOriginY = transform.parent != null ? transform.parent.TransformPoint(baseLocalPosition).y : baseLocalPosition.y;

            float lowestDrop = 0f;
            bool anyPlanted = false;

            ApplyFoot(left, originY, baseOriginY, dt, ref lowestDrop, ref anyPlanted);
            ApplyFoot(right, originY, baseOriginY, dt, ref lowestDrop, ref anyPlanted);

            // Sem nenhum pé apoiado (os dois no ar durante a corrida) mantém a altura do corpo.
            if (anyPlanted) pelvisTarget = lowestDrop;
        }

        private void ApplyFoot(Foot foot, float originY, float baseOriginY, float dt, ref float lowestDrop, ref bool anyPlanted)
        {
            Vector3 goalPosition = animator.GetIKPosition(foot.goal);
            Quaternion goalRotation = animator.GetIKRotation(foot.goal);

            SampleGround(foot, goalPosition, originY, dt);
            if (!foot.hasGround)
            {
                animator.SetIKPositionWeight(foot.goal, 0f);
                animator.SetIKRotationWeight(foot.goal, 0f);
                return;
            }

            // Altura do pé acima do apoio na própria animação: 0 = plantado, maior = levantado (passada, pulo).
            float lift = Mathf.Max(0f, goalPosition.y - (originY + footWorldHeight));
            float planted = 1f - Mathf.SmoothStep(0f, 1f, lift / (liftFade * scale * 2f));
            float weight = planted * ikWeight;

            // A animação supõe chão na altura do pivô: desloca o pé pela diferença real.
            Vector3 position = goalPosition + Vector3.up * (foot.groundY - originY);

            Quaternion tilt = Quaternion.RotateTowards(Quaternion.identity, Quaternion.FromToRotation(Vector3.up, foot.normal), maxFootTilt);

            animator.SetIKPositionWeight(foot.goal, weight);
            animator.SetIKPosition(foot.goal, position);
            animator.SetIKRotationWeight(foot.goal, weight);
            animator.SetIKRotation(foot.goal, tilt * goalRotation);

            // O corpo desce até o pé apoiado mais baixo (o de cima só dobra a perna).
            if (planted > 0.5f)
            {
                anyPlanted = true;
                lowestDrop = Mathf.Min(lowestDrop, foot.groundY - baseOriginY);
            }
        }

        private void SampleGround(Foot foot, Vector3 goalPosition, float originY, float dt)
        {
            Vector3 origin = new Vector3(goalPosition.x, Mathf.Max(goalPosition.y, originY) + rayStart * scale, goalPosition.z);
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, hits, rayLength * scale, groundMask, QueryTriggerInteraction.Ignore);

            float bestDistance = float.MaxValue;
            RaycastHit best = default;
            bool found = false;

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hits[i];

                // Ignora o próprio Player e paredes (troncos de árvore etc.); só superfícies onde dá para pisar.
                if (hit.collider.transform.IsChildOf(owner)) continue;
                if (hit.normal.y < MinWalkableNormalY) continue;

                if (hit.distance < bestDistance)
                {
                    bestDistance = hit.distance;
                    best = hit;
                    found = true;
                }
            }

            foot.hasGround = found;
            if (!found) return;

            if (!foot.initialized)
            {
                foot.groundY = best.point.y;
                foot.normal = best.normal;
                foot.initialized = true;
                return;
            }

            float k = 1f - Mathf.Exp(-groundFollow * dt);
            foot.groundY = Mathf.Lerp(foot.groundY, best.point.y, k);
            foot.normal = Vector3.Slerp(foot.normal, best.normal, k);
        }
    }
}
