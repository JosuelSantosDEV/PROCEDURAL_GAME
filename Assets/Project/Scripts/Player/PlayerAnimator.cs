using UnityEngine;

namespace PROCEDURAL_GAME.Player
{
    /// <summary>
    /// Liga o movimento do Player (<see cref="PlayerMovement"/>) ao Animator do modelo: velocidade (parado/andar/correr),
    /// no chão ou no ar e o disparo do pulo. Não move nada; só lê. O Animator Controller é criado por
    /// PROCEDURAL_GAME > Player > Setup Player Model.
    /// Parâmetros do controller: Speed (float), AnimSpeed (float, cadência da corrida), Grounded (bool), Jump (trigger).
    /// </summary>
    // Depois do PlayerMovement (100): usa a velocidade deste frame.
    [DefaultExecutionOrder(110)]
    [RequireComponent(typeof(PlayerMovement))]
    public class PlayerAnimator : MonoBehaviour
    {
        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int GaitId = Animator.StringToHash("Gait");
        private static readonly int AnimSpeedId = Animator.StringToHash("AnimSpeed");
        private static readonly int GroundedId = Animator.StringToHash("Grounded");
        private static readonly int JumpId = Animator.StringToHash("Jump");
        private static readonly int LandedId = Animator.StringToHash("Landed");
        private static readonly int MoveXId = Animator.StringToHash("MoveX");
        private static readonly int MoveYId = Animator.StringToHash("MoveY");
        private static readonly int AirPhaseId = Animator.StringToHash("AirPhase");
        private static readonly int StandingJumpId = Animator.StringToHash("StandingJump");

        // Velocidade (m/s, sem suavização) abaixo da qual o Player conta como parado / acima da qual conta como andando.
        private const float RestSpeed = 0.05f;

        // Por quanto tempo (s) "Landed" fica ligado depois de pousar: o Animator pode estar terminando outra transição (ex.: a de entrar no ar)
        // e só então consegue sair do estado de ar; sem essa folga o gatilho do pouso poderia ser perdido.
        private const float LandedWindow = 0.25f;

        // Queda sem pulo mais curta que isto (s): ressalto do terreno correndo. Não conta como pouso: a marcha não é interrompida.
        private const float ShortFallSeconds = 0.35f;

        [Tooltip("Animator do modelo. Se vazio, procura nos filhos.")]
        [SerializeField] private Animator animator;

        [Header("Passos (sem deslizar)")]
        [Tooltip("Velocidade (m/s) em que o clipe de andar, em ritmo normal, não desliza os pés. Medida no clipe pelo menu Setup Player Model (já na escala do Player).")]
        [SerializeField, Min(0.1f)] private float walkNaturalSpeed = 3f;

        [Tooltip("Idem para o clipe de correr.")]
        [SerializeField, Min(0.1f)] private float runNaturalSpeed = 8f;

        [Tooltip("Ritmo mínimo/máximo dos passos (1 = ritmo do clipe). Entre andar e correr o ritmo acompanha a velocidade real.")]
        [SerializeField, Range(0.3f, 1f)] private float minCadence = 0.45f;

        [SerializeField, Range(1f, 2f)] private float maxCadence = 1.5f;

        [Tooltip("Suavização (s) da troca de direção dos passos (frente, lados, trás) em primeira pessoa.")]
        [SerializeField, Min(0f)] private float directionSmoothing = 0.08f;

        [Header("Passos curtos (ajuste fino com a manete)")]
        [Tooltip("Velocidade mínima (m/s) usada na ANIMAÇÃO assim que há movimento: um toque leve na manete já mostra o passo de andar em vez de deslizar com o corpo parado.")]
        [SerializeField, Min(0f)] private float minStepSpeed = 1f;

        [Tooltip("Tempo mínimo (s) de animação de andar contado desde que o jogador começou a mexer: uma esticada rápida dá ao menos um passo. Quem já andou esse tempo todo pára de animar assim que solta a manete.")]
        [SerializeField, Min(0f)] private float minMoveSeconds = 0.4f;

        [Tooltip("Fração da velocidade de andar a partir da qual a animação já é 100% andar (abaixo disso mistura com o idle). Pequena = o passo aparece logo.")]
        [SerializeField, Range(0.02f, 0.5f)] private float stepBlendFraction = 0.12f;

        [Header("Pulo e queda (pose pela velocidade vertical)")]
        [Tooltip("Pontos do clipe de pulo (0 a 1) medidos pelo menu Setup Player Model: quando os pés saem do chão, o ponto mais alto e quando voltam a tocar.")]
        [SerializeField, Range(0f, 1f)] private float jumpLeavePhase = 0.3f;

        [SerializeField, Range(0f, 1f)] private float jumpApexPhase = 0.5f;

        [SerializeField, Range(0f, 1f)] private float jumpLandPhase = 0.75f;

        [Tooltip("Duração (s) do clipe de pulo.")]
        [SerializeField, Min(0.1f)] private float jumpClipLength = 0.87f;

        [Header("Pulo parado (clipe próprio, parte de um personagem parado)")]
        [Tooltip("Duração (s) do clipe de pulo parado. 0 = não há clipe: o pulo parado usa o mesmo clipe do pulo andando.")]
        [SerializeField, Min(0f)] private float standingClipLength;

        [SerializeField, Range(0f, 1f)] private float standingLeavePhase = 0.3f;

        [SerializeField, Range(0f, 1f)] private float standingApexPhase = 0.5f;

        [SerializeField, Range(0f, 1f)] private float standingLandPhase = 0.75f;

        [Tooltip("Depois de pousar, tempo (s) para a marcha sair do parado e chegar em andar/correr: a corrida só entra de vez quando o corpo já absorveu o pouso. Acompanha o fade do pouso (LandingFade no PlayerBuilder).")]
        [SerializeField, Min(0.01f)] private float landingGaitRamp = 0.25f;

        [Header("Giro parado (primeira pessoa)")]
        [Tooltip("Parado, quando o corpo gira para alcançar a câmera, a animação dá passos para o lado do giro (em vez de arrastar os pés). Fração da velocidade natural de andar usada nesses passos.")]
        [SerializeField, Range(0.2f, 1.5f)] private float turnStepFraction = 0.8f;

        [Header("Ajustes")]
        [Tooltip("Suavização da marcha ao começar a mexer, em segundos (evita trocar de parado para correndo de uma vez).")]
        [SerializeField, Min(0f)] private float speedDamping = 0.08f;

        [Tooltip("Suavização da marcha ao parar, em segundos. Menor = vai para o idle mais rápido ao soltar a manete.")]
        [SerializeField, Min(0f)] private float stopDamping = 0.03f;

        [Tooltip("Tempo no ar (s) antes de contar como 'não está no chão'. Em terreno irregular o CharacterController perde o chão por 1 ou 2 frames sem a pessoa estar caindo.")]
        [SerializeField, Min(0f)] private float groundedGrace = 0.12f;

        private PlayerMovement movement;
        private float airTime;
        private float directionAngle;
        private float targetAngle;
        private float directionVelocity;
        private float moveHold;
        private bool airActive;
        private float airPhase;
        private float landedUntil;
        private bool airFromJump;
        private float airStart;
        private float gaitRampEnd;
        private float jumpedAt = -10f;
        private bool standingJump;
        private bool windingUp;
        private float windUpEnd;
        private float windUpSeconds;
        private float moveTime;

        private void Awake()
        {
            movement = GetComponent<PlayerMovement>();
            if (animator == null) animator = GetComponentInChildren<Animator>();

            if (animator == null)
            {
                Debug.LogWarning($"{nameof(PlayerAnimator)}: nenhum Animator encontrado. Rode PROCEDURAL_GAME > Player > Setup Player Model.", this);
                enabled = false;
                return;
            }

            // O movimento é do CharacterController; a animação não pode empurrar o Player (root motion).
            animator.applyRootMotion = false;
        }

        private void OnEnable()
        {
            movement.Jumped += OnJumped;
            movement.WindUpStarted += OnWindUpStarted;
        }

        private void OnDisable()
        {
            movement.Jumped -= OnJumped;
            movement.WindUpStarted -= OnWindUpStarted;
        }

        private void Update()
        {
            float speed = movement.HorizontalSpeed;
            float dt = Time.deltaTime;

            // Velocidade da ANIMAÇÃO. Com a manete só encostada (velocidade pequena) ou solta logo depois, o corpo se move/pára mas a pose
            // não podia ficar no idle (deslizava): qualquer movimento vale pelo menos minStepSpeed e dura pelo menos minMoveSeconds.
            // O tempo já andando conta: só a esticada curta ganha um resto de animação ao soltar; quem andou o suficiente pára de animar na hora.
            // Giro parado em 1ª pessoa (o corpo alcança a câmera): conta como movimento para o lado do giro, senão os pés arrastam.
            int turn = movement.TurnInPlace;
            bool turning = turn != 0 && speed <= RestSpeed;
            bool moving = speed > RestSpeed || turning;

            if (moving)
            {
                moveTime += dt;
                moveHold = Mathf.Max(0f, minMoveSeconds - moveTime);
            }
            else
            {
                moveTime = 0f;
                moveHold = Mathf.Max(0f, moveHold - dt);
            }

            float stepSpeed = turning ? walkNaturalSpeed * turnStepFraction : speed;
            float animated = moving || moveHold > 0f ? Mathf.Max(stepSpeed, minStepSpeed) : 0f;

            // Speed só decide as transições idle <-> locomoção (com histerese): sem suavização, para sair do movimento no mesmo frame.
            // A marcha (Gait) mistura as poses: sobe suave e desce mais rápido.
            animator.SetFloat(SpeedId, animated);
            UpdateGait(GetGait(animated), dt);

            // Ritmo dos passos = velocidade da animação / velocidade natural do clipe naquela faixa (entre andar e correr interpola): sem deslizar.
            float cadence = 1f;
            if (animated > 0.5f)
            {
                float t = Mathf.InverseLerp(movement.WalkSpeed, movement.SprintSpeed, animated);
                float natural = Mathf.Lerp(walkNaturalSpeed, runNaturalSpeed, t);
                cadence = Mathf.Clamp(animated / natural, minCadence, maxCadence);
            }

            animator.SetFloat(AnimSpeedId, cadence);

            UpdateDirection(speed, turn);

            airTime = movement.IsGrounded ? 0f : airTime + Time.deltaTime;
            bool grounded = airTime < groundedGrace;
            animator.SetBool(GroundedId, grounded);

            UpdateAir(grounded, dt);
        }

        // Marcha (Gait). No ar fica em zero (uma corrida esperando pronta entrava de uma vez ao tocar o chão); ao pousar sobe em landingGaitRamp,
        // junto com o fade do pouso. Um ressalto curto do terreno correndo não interrompe a marcha.
        private void UpdateGait(float target, float dt)
        {
            float current = animator.GetFloat(GaitId);

            if (airActive && IsRealAir())
            {
                animator.SetFloat(GaitId, 0f, stopDamping, dt);
            }
            else if (Time.time < gaitRampEnd && target > current)
            {
                animator.SetFloat(GaitId, Mathf.MoveTowards(current, target, target / landingGaitRamp * dt));
            }
            else
            {
                animator.SetFloat(GaitId, target, target > current ? speedDamping : stopDamping, dt);
            }
        }

        private bool IsRealAir() => airFromJump || Time.time - airStart >= ShortFallSeconds;

        // Pontos do clipe em uso: o do pulo parado (que parte de um personagem parado) ou o do pulo andando/correndo.
        private float Leave => standingJump ? standingLeavePhase : jumpLeavePhase;
        private float Apex => standingJump ? standingApexPhase : jumpApexPhase;
        private float Land => standingJump ? standingLandPhase : jumpLandPhase;
        private float ClipLength => standingJump && standingClipLength > 0f ? standingClipLength : jumpClipLength;

        /// <summary>
        /// Pose de pulo e de queda. Um único estado do Animator toca o clipe de pulo com o tempo (AirPhase, 0 a 1) ditado por AQUI, não pelo
        /// relógio: no ar o ponto do clipe segue a velocidade vertical (subindo = decolagem até o ápice; descendo = do ápice até os pés
        /// tocando), então um pulo longo (descida, queda de borda) ou curto (subida) fica sempre coerente, sem saltos de pose.
        /// Ao tocar o chão liga "Landed" e o Animator começa NA HORA o fade para o que a manete pede (idle, andar ou correr). Durante o fade o
        /// resto do clipe (os joelhos absorvendo o pouso) continua tocando, em vez de a pose ficar parada: é o que evita o "teleporte".
        /// </summary>
        private void UpdateAir(bool grounded, float dt)
        {
            // Preparação do pulo parado: o corpo agacha (do começo do clipe até o ponto em que os pés saem do chão) e então o pulo sai.
            if (windingUp)
            {
                if (Time.time > windUpEnd || !movement.IsGrounded)
                {
                    windingUp = false;
                }
                else
                {
                    airPhase = Mathf.MoveTowards(airPhase, Leave, dt * Leave / windUpSeconds);
                    animator.SetFloat(AirPhaseId, airPhase);
                    animator.SetBool(LandedId, false);
                    return;
                }
            }

            // Logo após pular o Grounded ainda vale (tolerância de terreno): não é pouso.
            bool takingOff = Time.time - jumpedAt < 0.25f;

            if (!grounded || takingOff)
            {
                if (!airActive)
                {
                    airActive = true;
                    airFromJump = takingOff;
                    airStart = Time.time;

                    // Queda sem pulo (borda): usa o clipe/pontos do pulo andando.
                    if (!takingOff) SetStandingJump(false);
                }

                landedUntil = 0f;

                float v = movement.VerticalVelocity;
                float v0 = Mathf.Max(1f, movement.JumpVelocity);
                airPhase = v >= 0f
                    ? Mathf.Lerp(Leave, Apex, 1f - Mathf.Clamp01(v / v0))
                    : Mathf.Lerp(Apex, Land, Mathf.Clamp01(-v / v0));
            }
            else if (airActive)
            {
                // Pousou. Só um pouso de verdade segura a marcha; o ressalto curto segue correndo.
                if (IsRealAir()) gaitRampEnd = Time.time + landingGaitRamp;

                airActive = false;
                landedUntil = Time.time + LandedWindow;
            }

            // No chão o clipe segue até o fim no ritmo dele. O valor não é reposto: o estado de ar ainda aparece durante o fade do pouso.
            if (!airActive) airPhase = Mathf.MoveTowards(airPhase, 1f, dt / ClipLength);

            animator.SetBool(LandedId, Time.time < landedUntil);
            animator.SetFloat(AirPhaseId, airPhase);
        }

        private void SetStandingJump(bool value)
        {
            standingJump = value;
            animator.SetBool(StandingJumpId, value);
        }

        // Posição na mistura de marcha: 0 = idle, 1 = andar, 2 = correr. Qualquer movimento chega logo em "andar" (nada de ficar meio idle,
        // meio andar, que parece deslizar); andar -> correr acompanha a velocidade.
        private float GetGait(float animated)
        {
            if (animated <= RestSpeed) return 0f;

            float walk = movement.WalkSpeed;
            if (animated <= walk) return Mathf.Clamp01(animated / Mathf.Max(0.01f, walk * stepBlendFraction));

            return 1f + Mathf.InverseLerp(walk, movement.SprintSpeed, animated);
        }

        /// <summary>
        /// Direção do movimento em relação ao corpo, para a mistura de 8 direções (MoveX = direita, MoveY = frente). Em terceira pessoa o
        /// corpo já vira para onde anda: é sempre "frente". Em primeira o corpo olha para a câmera e o Player anda de frente, de ré e de lado.
        /// A direção é um ângulo suavizado (passa pelos lados ao inverter, nunca por um vetor nulo, que a mistura não sabe interpretar).
        /// </summary>
        private void UpdateDirection(float speed, int turn)
        {
            if (!movement.FaceCamera)
            {
                targetAngle = 0f;
            }
            else if (speed > RestSpeed)
            {
                Vector3 local = transform.InverseTransformDirection(movement.HorizontalVelocity / speed);
                targetAngle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            }
            else if (turn != 0)
            {
                // Parado e girando: passos para o lado do giro (direita = +90°, esquerda = -90°).
                targetAngle = turn * 90f;
            }

            directionAngle = Mathf.SmoothDampAngle(directionAngle, targetAngle, ref directionVelocity, directionSmoothing);
            float radians = directionAngle * Mathf.Deg2Rad;
            animator.SetFloat(MoveXId, Mathf.Sin(radians));
            animator.SetFloat(MoveYId, Mathf.Cos(radians));
        }

        // Pulo parado pedido: começa a preparação (agachar). O clipe parte do começo, com o personagem parado.
        private void OnWindUpStarted()
        {
            windingUp = true;
            windUpSeconds = Mathf.Max(0.05f, movement.StandingJumpWindUp);
            windUpEnd = Time.time + windUpSeconds + 0.2f;

            airActive = true;
            airFromJump = true;
            airStart = Time.time;
            landedUntil = 0f;
            airPhase = 0f;

            SetStandingJump(true);
            animator.SetTrigger(JumpId);
        }

        private void OnJumped()
        {
            jumpedAt = Time.time;
            landedUntil = 0f;

            // Saiu do chão depois da preparação: o gatilho já foi disparado ao agachar.
            if (windingUp)
            {
                windingUp = false;
                return;
            }

            // Parado, sem preparação configurada: ainda assim usa o clipe do pulo parado (se houver); andando/correndo usa o de sempre.
            SetStandingJump(standingClipLength > 0f && movement.HorizontalSpeed <= RestSpeed);
            animator.SetTrigger(JumpId);
        }
    }
}
