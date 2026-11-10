using UnityEngine;

namespace PROCEDURAL_GAME.Player
{
    /// <summary>
    /// Move o personagem com CharacterController: deslocamento horizontal, rotação e gravidade.
    /// O input vem do <see cref="PlayerInputReader"/>.
    /// </summary>
    // Ordem 100: roda depois do EventSystem (ordem 0), que é quem entrega os toques da tela neste frame.
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(PlayerInputReader))]
    public class PlayerMovement : MonoBehaviour
    {
        // Velocidade horizontal (m/s) até a qual o Player conta como parado para o pulo.
        private const float StandingSpeed = 0.05f;

        [Header("Movimento")]
        [Tooltip("Velocidade ao andar, em metros por segundo.")]
        [SerializeField, Min(0f)] private float walkSpeed = 5f;

        [Tooltip("Velocidade ao correr (segurando Shift / clique do analógico esquerdo), em metros por segundo.")]
        [SerializeField, Min(0f)] private float sprintSpeed = 8f;

        [Tooltip("Quão rápido o personagem vira para a direção do movimento, em graus por segundo.")]
        [SerializeField, Min(0f)] private float rotationSpeed = 720f;

        [Tooltip("Durante um ataque: velocidade (graus por segundo) com que o corpo vira para onde a manete aponta (3ª pessoa) ou para a câmera (1ª). Mais lenta que a normal, para o golpe mudar de direção sem o corpo girar de uma vez.")]
        [SerializeField, Min(0f)] private float actionTurnSpeed = 450f;

        [Header("Pulo")]
        [Tooltip("Altura máxima do pulo, em metros. A velocidade inicial é calculada a partir dela e da gravidade.")]
        [SerializeField, Min(0f)] private float jumpHeight = 1.5f;

        [Tooltip("Altura do pulo em movimento (andando ou correndo), em metros. Menor que a do pulo parado: somada à velocidade para a frente, a subida ficava íngreme.")]
        [SerializeField, Min(0f)] private float movingJumpHeight = 1.1f;

        [Tooltip("Gravidade só durante o pulo em movimento (m/s², negativa). Mais fraca = saída do chão com menos velocidade, subida mais suave.")]
        [SerializeField] private float movingJumpGravity = -11f;

        [Header("Gravidade")]
        [Tooltip("Aceleração da gravidade (negativa = para baixo), em m/s².")]
        [SerializeField] private float gravity = -20f;

        [Tooltip("Pequena força para baixo enquanto está no chão. Mantém o personagem 'colado' em rampas.")]
        [SerializeField] private float groundedStickVelocity = -2f;

        [Header("Pulo parado")]
        [Tooltip("Pulando PARADO, o corpo agacha (preparação do clipe de pulo parado) por este tempo (s) antes de sair do chão. 0 = sai na hora. Andando ou correndo, o pulo sai na hora. O menu Setup Player Model grava o valor pelo clipe.")]
        [SerializeField, Range(0f, 1.5f)] private float standingJumpWindUp = 0f;

        [Header("Rolagem (fim do pulo em movimento, 3ª pessoa)")]
        [Tooltip("Pulo em movimento termina rolando: sobe com o clipe de pulo e desce com o Falling To Roll, que rola ao tocar o chão.")]
        [SerializeField] private bool rollAfterMovingJump = true;

        [Tooltip("Duração (s) do clipe Falling To Roll. 0 = sem clipe: o pulo em movimento pousa normal. O menu Setup Player Model grava.")]
        [SerializeField, Min(0f)] private float rollClipLength;

        [Tooltip("Ponto do clipe (0 a 1) em que o corpo toca o chão. Medido pelo menu.")]
        [SerializeField, Range(0f, 1f)] private float rollImpactPhase = 0.25f;

        [Tooltip("Ponto do clipe (0 a 1) em que o corpo já está de pé: a partir daqui o jogador volta a controlar (o resto do clipe vira o fade para andar/parado). Medido pelo menu.")]
        [SerializeField, Range(0f, 1f)] private float rollExitPhase = 0.85f;

        [Tooltip("Metros percorridos para a frente ao longo do clipe (x = ponto 0 a 1, y = m, já na escala do Player), medidos no clipe pelo menu. O deslocamento da rolagem sai daqui, então acompanha a animação. Vazia ou plana = clipe In Place: a rolagem usa o embalo do pouso.")]
        [SerializeField] private AnimationCurve rollDistance = new AnimationCurve();

        [Tooltip("Velocidade média (m/s) da rolagem no ritmo normal do clipe, entre o toque no chão e ficar de pé. Medida pelo menu.")]
        [SerializeField, Min(0.1f)] private float rollNaturalSpeed = 4f;

        [Tooltip("Ritmo da rolagem (1 = o do clipe) acompanha a velocidade do pouso dentro destes limites: correndo rola mais rápido e mais longe.")]
        [SerializeField] private Vector2 rollPlaybackRange = new Vector2(0.85f, 1.3f);

        [Tooltip("Só rola se, ao pular, a velocidade era pelo menos esta fração da de andar (uma esticada leve da manete pousa normal).")]
        [SerializeField, Range(0f, 1f)] private float rollMinSpeedFraction = 0.5f;

        [Header("Primeira pessoa")]
        [Tooltip("Parado, o corpo só gira quando a câmera passa deste ângulo (graus) em relação a ele: o 'pescoço' aguenta isso. Andando, o corpo acompanha a câmera sempre.")]
        [SerializeField, Range(10f, 120f)] private float neckLimit = 50f;

        [Tooltip("Velocidade (graus por segundo) com que o corpo alcança a câmera parado, depois de passar do limite do pescoço.")]
        [SerializeField, Min(0f)] private float idleTurnSpeed = 300f;

        [Header("Referências")]
        [Tooltip("Câmera que define o que é 'frente'. Se vazio, usa a Main Camera.")]
        [SerializeField] private Transform cameraTransform;

        private CharacterController controller;
        private PlayerInputReader input;
        private float verticalVelocity;
        private float horizontalSpeed;
        private float activeGravity;
        private float activeJumpVelocity;
        private Vector3 horizontalVelocity;
        private bool turningToCamera;
        private int turnInPlace;
        private bool windingUp;
        private float windUpTimer;
        private bool jumpRequested;
        private float requestedSpeed;
        private bool rollArmed;
        private bool rolling;
        private float rollPhase;
        private float rollPlayback = 1f;
        private float rollLandingSpeed;
        private Vector3 rollDirection;

        /// <summary>
        /// Em primeira pessoa o corpo olha para onde a câmera olha (e o Player anda de frente, de ré e de lado); em terceira o corpo vira
        /// para a direção do movimento. Quem liga é o PlayerCameraController ao trocar de modo.
        /// </summary>
        public bool FaceCamera { get; set; }

        /// <summary>
        /// Primeira pessoa, parado: +1 enquanto o corpo gira para a direita para alcançar a câmera, -1 para a esquerda, 0 se não gira.
        /// O PlayerAnimator usa para dar passos na direção do giro em vez de arrastar os pés.
        /// </summary>
        public int TurnInPlace => turnInPlace;

        /// <summary>Velocidade horizontal desejada neste frame (vetor, m/s, no mundo).</summary>
        public Vector3 HorizontalVelocity => horizontalVelocity;

        /// <summary>Velocidade de andar (m/s).</summary>
        public float WalkSpeed => walkSpeed;

        /// <summary>Velocidade de correr (m/s).</summary>
        public float SprintSpeed => sprintSpeed;

        /// <summary>O corpo está preso a uma ação (atacar, coletar, morrer): não anda e não pula (o ataque ainda gira, ver <see cref="TurnWhileLocked"/>). Quem liga é o PlayerActions.</summary>
        public bool ActionLocked { get; set; }

        /// <summary>
        /// Com <see cref="ActionLocked"/>: o corpo continua sem andar, mas pode virar (a <see cref="actionTurnSpeed"/>). O PlayerActions liga durante
        /// os ataques, para o jogador mudar a direção do golpe; coletar e morrer continuam presos.
        /// </summary>
        public bool TurnWhileLocked { get; set; }

        /// <summary>Sem energia: o pedido de correr é ignorado (quem liga é o PlayerStats).</summary>
        public bool SprintBlocked { get; set; }

        /// <summary>Está correndo de fato neste frame: com a corrida ligada, liberada e em movimento. É o que gasta energia.</summary>
        public bool IsSprintMoving => !rolling && horizontalSpeed > StandingSpeed && input.Sprinting && !SprintBlocked;

        /// <summary>Velocidade (m/s) que a manete pede agora, mesmo com o corpo preso (ação, rolagem): decide para onde a animação vai depois.</summary>
        public float RequestedSpeed => requestedSpeed;

        /// <summary>No ar num pulo que vai terminar rolando (o PlayerAnimator troca a pose da descida pela do Falling To Roll).</summary>
        public bool RollArmed => rollArmed;

        /// <summary>Rolando no chão: o deslocamento vem da animação e o jogador não controla até ficar de pé.</summary>
        public bool IsRolling => rolling;

        /// <summary>Ponto atual (0 a 1) do clipe de rolagem durante a rolagem. É o mesmo usado no deslocamento: pose e movimento andam juntos.</summary>
        public float RollPhase => rollPhase;

        /// <summary>Ritmo atual da rolagem (1 = o do clipe).</summary>
        public float RollPlayback => rollPlayback;

        public float RollImpactPhase => rollImpactPhase;

        public float RollClipLength => rollClipLength;

        /// <summary>Disparado quando a rolagem termina e o jogador volta a controlar.</summary>
        public event System.Action RollEnded;

        /// <summary>
        /// Se um novo pulo é aceito agora. O PlayerAnimator desliga do início do pulo até o fim da animação de pouso: toques nesse intervalo são
        /// descartados, não guardados para depois.
        /// </summary>
        public bool JumpAllowed { get; set; } = true;

        /// <summary>Disparado quando o jogador pede um pulo PARADO e o corpo começa a agachar (o pulo em si sai depois, em <see cref="Jumped"/>).</summary>
        public event System.Action WindUpStarted;

        /// <summary>Duração (s) da preparação do pulo parado.</summary>
        public float StandingJumpWindUp => standingJumpWindUp;

        /// <summary>Disparado no frame em que o Player sai do chão pulando (o <see cref="PlayerAnimator"/> usa para tocar a animação).</summary>
        public event System.Action Jumped;

        /// <summary>Velocidade horizontal desejada neste frame, em m/s (0 parado, andar/correr conforme o analógico).</summary>
        public float HorizontalSpeed => horizontalSpeed;

        /// <summary>Velocidade vertical inicial do pulo (m/s). O PlayerAnimator mapeia a velocidade vertical atual para a pose do clipe.</summary>
        public float JumpVelocity => activeJumpVelocity;

        /// <summary>Velocidade vertical em m/s (positiva subindo).</summary>
        public float VerticalVelocity => verticalVelocity;

        /// <summary>Pés no chão (resultado do último Move).</summary>
        public bool IsGrounded => controller != null && controller.isGrounded;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            input = GetComponent<PlayerInputReader>();
            ResetJumpPhysics();

            if (cameraTransform == null && Camera.main != null)
            {
                cameraTransform = Camera.main.transform;
            }
        }

        /// <summary>
        /// Pede um pulo que sai no próximo frame, sem a preparação do pulo parado. É o pulo guardado durante um ataque, liberado pelo
        /// PlayerActions quando o golpe chega à recuperação. Vale só para o frame seguinte (nada fica pendurado para depois).
        /// </summary>
        public void RequestJump() => jumpRequested = true;

        private void Update()
        {
            horizontalVelocity = GetHorizontalVelocity();
            horizontalSpeed = horizontalVelocity.magnitude;
            UpdateVerticalVelocity();

            Vector3 velocity = horizontalVelocity + Vector3.up * verticalVelocity;
            controller.Move(velocity * Time.deltaTime);

            if (rolling)
            {
                // A direção da rolagem foi decidida ao tocar o chão.
                turningToCamera = false;
                turnInPlace = 0;
            }
            else if (ActionLocked)
            {
                turningToCamera = false;
                turnInPlace = 0;
                if (TurnWhileLocked) RotateDuringAction();
            }
            else if (FaceCamera && cameraTransform != null)
            {
                RotateTowardsCamera(horizontalVelocity);
            }
            else
            {
                turningToCamera = false;
                turnInPlace = 0;
                RotateTowards(horizontalVelocity);
            }
        }

        private Vector3 GetHorizontalVelocity()
        {
            Vector2 move = input.Move;
            Vector3 direction = ToCameraSpace(move);

            // ClampMagnitude evita que andar na diagonal seja mais rápido que em linha reta.
            float speed = input.Sprinting && !SprintBlocked ? sprintSpeed : walkSpeed;
            Vector3 desired = Vector3.ClampMagnitude(direction, 1f) * speed;
            requestedSpeed = desired.magnitude;

            // Morrer no meio da rolagem para tudo.
            if (rolling && ActionLocked) rolling = false;

            if (rolling && TryGetRollVelocity(out Vector3 rollVelocity)) return rollVelocity;

            // Preso a uma ação ou preparando o pulo parado: o corpo não desliza, mesmo com a manete inclinada.
            if (ActionLocked || windingUp) return Vector3.zero;

            return desired;
        }

        /// <summary>
        /// Avança a rolagem e devolve a velocidade dela. O deslocamento é a diferença da curva de distância medida no clipe entre o ponto
        /// anterior e o atual: o corpo anda exatamente o que a animação mostra. Falso quando a rolagem acabou (o jogador volta a controlar
        /// neste mesmo frame).
        /// </summary>
        private bool TryGetRollVelocity(out Vector3 velocity)
        {
            velocity = Vector3.zero;
            float dt = Time.deltaTime;
            if (dt <= 0f) return true;

            float previous = rollPhase;
            rollPhase = Mathf.Min(1f, rollPhase + dt * rollPlayback / Mathf.Max(0.1f, rollClipLength));

            if (rollPhase >= rollExitPhase)
            {
                rolling = false;
                RollEnded?.Invoke();
                return false;
            }

            float meters;
            if (HasRollMotion)
            {
                meters = rollDistance.Evaluate(rollPhase) - rollDistance.Evaluate(previous);
            }
            else
            {
                // Clipe In Place: o embalo do pouso vai a zero até ficar de pé.
                float progress = Mathf.InverseLerp(rollImpactPhase, rollExitPhase, rollPhase);
                meters = rollLandingSpeed * (1f - progress) * dt;
            }

            velocity = rollDirection * (Mathf.Max(0f, meters) / dt);
            return true;
        }

        private bool HasRollMotion => rollDistance != null && rollDistance.length >= 2 &&
                                      rollDistance.Evaluate(rollExitPhase) - rollDistance.Evaluate(rollImpactPhase) > 0.3f;

        private bool CanRoll => rollAfterMovingJump && rollClipLength > 0f && !FaceCamera;

        private void StartRoll()
        {
            rolling = true;
            rollPhase = rollImpactPhase;
            rollLandingSpeed = horizontalSpeed;
            rollPlayback = Mathf.Clamp(rollLandingSpeed / rollNaturalSpeed, rollPlaybackRange.x, rollPlaybackRange.y);

            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            rollDirection = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        }

        /// <summary>
        /// Converte o input (X = lados, Y = frente/trás) para o mundo, usando a direção da câmera.
        /// Achatamos no plano horizontal: olhar para o chão não deve fazer o personagem afundar.
        /// </summary>
        private Vector3 ToCameraSpace(Vector2 move)
        {
            if (cameraTransform == null) return new Vector3(move.x, 0f, move.y);

            Vector3 forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;

            return right * move.x + forward * move.y;
        }

        private void UpdateVerticalVelocity()
        {
            // isGrounded reflete o resultado do Move do frame anterior.
            bool isGrounded = controller.isGrounded;

            if (isGrounded && verticalVelocity < 0f)
            {
                verticalVelocity = groundedStickVelocity;
                ResetJumpPhysics();

                // Pousou de um pulo em movimento: rola. Trocou para a 1ª pessoa no ar: pousa normal.
                if (rollArmed)
                {
                    rollArmed = false;
                    if (CanRoll && !ActionLocked) StartRoll();
                }
            }

            // O pedido de pulo vale só neste frame.
            bool jumpPressed = input.JumpPressed || jumpRequested;
            bool skipWindUp = jumpRequested;
            jumpRequested = false;

            if (windingUp)
            {
                // Preparação do pulo parado: agacha e então sai do chão (cancela se o chão some, por exemplo ao cair de uma borda).
                windUpTimer -= Time.deltaTime;
                if (!isGrounded) windingUp = false;
                else if (windUpTimer <= 0f)
                {
                    windingUp = false;
                    Launch(jumpHeight, gravity);
                }
            }
            else if (isGrounded && JumpAllowed && !ActionLocked && !rolling && jumpPressed)
            {
                // Só pula com os pés no chão; a gravidade abaixo se encarrega de trazer de volta.
                if (!skipWindUp && standingJumpWindUp > 0f && horizontalSpeed <= StandingSpeed)
                {
                    windingUp = true;
                    windUpTimer = standingJumpWindUp;
                    WindUpStarted?.Invoke();
                }
                else if (horizontalSpeed > StandingSpeed)
                {
                    // Antes do Launch: quem ouve o Jumped já sabe se este pulo termina rolando.
                    rollArmed = CanRoll && horizontalSpeed >= walkSpeed * rollMinSpeedFraction;
                    Launch(movingJumpHeight, movingJumpGravity);
                }
                else
                {
                    Launch(jumpHeight, gravity);
                }
            }

            verticalVelocity += activeGravity * Time.deltaTime;
        }

        private void Launch(float height, float jumpGravity)
        {
            activeGravity = jumpGravity;
            activeJumpVelocity = GetJumpVelocity(height, jumpGravity);
            verticalVelocity = activeJumpVelocity;
            Jumped?.Invoke();
        }

        // No chão e em queda sem pulo (borda) valem a gravidade e o pulo normais.
        private void ResetJumpPhysics()
        {
            activeGravity = gravity;
            activeJumpVelocity = GetJumpVelocity(jumpHeight, gravity);
        }

        /// <summary>
        /// Física: para subir uma altura h com gravidade g, a velocidade inicial é v = √(2·h·|g|).
        /// </summary>
        private static float GetJumpVelocity(float height, float jumpGravity) => Mathf.Sqrt(2f * height * -jumpGravity);

        /// <summary>
        /// Primeira pessoa: o corpo acompanha o giro da câmera (o jogador não gira o pescoço 360°). Andando, alinha rápido; parado, só gira
        /// depois que a câmera passa do limite do pescoço e então alcança a câmera (não há animação de giro no lugar: parado o corpo só
        /// precisa acompanhar de vez em quando).
        /// </summary>
        private void RotateTowardsCamera(Vector3 velocity)
        {
            float cameraYaw = cameraTransform.eulerAngles.y;
            float difference = Mathf.DeltaAngle(transform.eulerAngles.y, cameraYaw);
            float speed;

            if (velocity.sqrMagnitude > 0.0001f)
            {
                turningToCamera = false;
                turnInPlace = 0;
                speed = rotationSpeed;
            }
            else
            {
                if (Mathf.Abs(difference) > neckLimit) turningToCamera = true;
                else if (Mathf.Abs(difference) < 2f) turningToCamera = false;

                if (!turningToCamera)
                {
                    turnInPlace = 0;
                    return;
                }

                turnInPlace = difference > 0f ? 1 : -1;
                speed = idleTurnSpeed;
            }

            float yaw = Mathf.MoveTowardsAngle(transform.eulerAngles.y, cameraYaw, speed * Time.deltaTime);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        // Ataque: 1ª pessoa = o corpo acompanha a câmera (o golpe vai para onde se olha); 3ª = vira para onde a manete aponta, sem andar.
        // Manete solta em 3ª pessoa: o corpo fica como está.
        private void RotateDuringAction()
        {
            Quaternion target;

            if (FaceCamera && cameraTransform != null)
            {
                target = Quaternion.Euler(0f, cameraTransform.eulerAngles.y, 0f);
            }
            else
            {
                Vector3 direction = ToCameraSpace(input.Move);
                if (direction.sqrMagnitude < 0.01f) return;
                target = Quaternion.LookRotation(direction);
            }

            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, actionTurnSpeed * Time.deltaTime);
        }

        private void RotateTowards(Vector3 horizontalVelocity)
        {
            if (horizontalVelocity.sqrMagnitude < 0.0001f) return;

            Quaternion target = Quaternion.LookRotation(horizontalVelocity);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, rotationSpeed * Time.deltaTime);
        }
    }
}
