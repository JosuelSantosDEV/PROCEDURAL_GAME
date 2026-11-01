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
        [Header("Movimento")]
        [Tooltip("Velocidade ao andar, em metros por segundo.")]
        [SerializeField, Min(0f)] private float walkSpeed = 5f;

        [Tooltip("Velocidade ao correr (segurando Shift / clique do analógico esquerdo), em metros por segundo.")]
        [SerializeField, Min(0f)] private float sprintSpeed = 8f;

        [Tooltip("Quão rápido o personagem vira para a direção do movimento, em graus por segundo.")]
        [SerializeField, Min(0f)] private float rotationSpeed = 720f;

        [Header("Pulo")]
        [Tooltip("Altura máxima do pulo, em metros. A velocidade inicial é calculada a partir dela e da gravidade.")]
        [SerializeField, Min(0f)] private float jumpHeight = 1.5f;

        [Header("Gravidade")]
        [Tooltip("Aceleração da gravidade (negativa = para baixo), em m/s².")]
        [SerializeField] private float gravity = -20f;

        [Tooltip("Pequena força para baixo enquanto está no chão. Mantém o personagem 'colado' em rampas.")]
        [SerializeField] private float groundedStickVelocity = -2f;

        [Header("Referências")]
        [Tooltip("Câmera que define o que é 'frente'. Se vazio, usa a Main Camera.")]
        [SerializeField] private Transform cameraTransform;

        private CharacterController controller;
        private PlayerInputReader input;
        private float verticalVelocity;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            input = GetComponent<PlayerInputReader>();

            if (cameraTransform == null && Camera.main != null)
            {
                cameraTransform = Camera.main.transform;
            }
        }

        private void Update()
        {
            Vector3 horizontalVelocity = GetHorizontalVelocity();
            UpdateVerticalVelocity();

            Vector3 velocity = horizontalVelocity + Vector3.up * verticalVelocity;
            controller.Move(velocity * Time.deltaTime);

            RotateTowards(horizontalVelocity);
        }

        private Vector3 GetHorizontalVelocity()
        {
            Vector2 move = input.Move;
            Vector3 direction = ToCameraSpace(move);

            // ClampMagnitude evita que andar na diagonal seja mais rápido que em linha reta.
            float speed = input.SprintHeld ? sprintSpeed : walkSpeed;
            return Vector3.ClampMagnitude(direction, 1f) * speed;
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
            }

            // Só pula com os pés no chão; a gravidade abaixo se encarrega de trazer de volta.
            if (isGrounded && input.JumpPressed)
            {
                verticalVelocity = GetJumpVelocity();
            }

            verticalVelocity += gravity * Time.deltaTime;
        }

        /// <summary>
        /// Física: para subir uma altura h com gravidade g, a velocidade inicial é v = √(2·h·|g|).
        /// </summary>
        private float GetJumpVelocity() => Mathf.Sqrt(2f * jumpHeight * -gravity);

        private void RotateTowards(Vector3 horizontalVelocity)
        {
            if (horizontalVelocity.sqrMagnitude < 0.0001f) return;

            Quaternion target = Quaternion.LookRotation(horizontalVelocity);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, rotationSpeed * Time.deltaTime);
        }
    }
}
