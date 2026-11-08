using PROCEDURAL_GAME.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace PROCEDURAL_GAME.CameraControl
{
    public enum CameraMode
    {
        FirstPerson,
        ThirdPerson
    }

    /// <summary>
    /// Câmera do jogador, com alternância entre primeira e terceira pessoa.
    /// Vai no objeto Main Camera. Guarda yaw/pitch (para onde olha) e posiciona a câmera em torno do Player.
    /// </summary>
    // Ordem 100: roda depois do EventSystem (ordem 0), que é quem entrega os toques da tela neste frame.
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(Camera))]
    public class PlayerCameraController : MonoBehaviour
    {
        private const int HitBufferSize = 8;
        private const float MinThirdPersonDistance = 0.3f;

        [Header("Alvo")]
        [Tooltip("Se vazio, procura o objeto com a tag 'Player'.")]
        [SerializeField] private Transform target;

        [Header("Modo")]
        [SerializeField] private CameraMode startMode = CameraMode.ThirdPerson;

        [Header("Olhar")]
        [Tooltip("Graus por pixel de movimento do mouse.")]
        [SerializeField, Min(0f)] private float mouseSensitivity = 0.1f;

        [Tooltip("Graus por segundo com o analógico totalmente inclinado.")]
        [SerializeField, Min(0f)] private float gamepadSensitivity = 150f;

        [Tooltip("Graus por pixel de arrasto do dedo na tela.")]
        [SerializeField, Min(0f)] private float touchSensitivity = 0.15f;

        [Tooltip("Trava o cursor para olhar com o mouse (teste no PC). Desmarque para testar os controles de toque com o mouse no Editor.")]
        [SerializeField] private bool lockCursor = true;

        [Tooltip("Limite ao olhar para cima (valor negativo).")]
        [SerializeField, Range(-89f, 0f)] private float minPitch = -80f;

        [Tooltip("Limite ao olhar para baixo.")]
        [SerializeField, Range(0f, 89f)] private float maxPitch = 80f;

        [Header("Primeira pessoa")]
        [Tooltip("Altura dos olhos acima do pivô do Player (a cápsula tem o pivô no centro).")]
        [SerializeField] private float eyeHeight = 0.6f;

        [Tooltip("Com o corpo visível em primeira pessoa: avanço EXTRA (m) da câmera à frente dos olhos do modelo (que já são medidos no osso da cabeça). Mais = mais longe do pescoço e menos corpo à vista; 0 = exatamente nos olhos.")]
        [SerializeField, Min(0f)] private float firstPersonForwardOffset = 0.1f;

        [Tooltip("Com o corpo visível: limite ao olhar para baixo (graus). Mais que isso a câmera passaria do pescoço (aberto, sem cabeça). Com 60° e campo de visão vertical de 60° a borda de baixo da tela chega a 90°: se vê peito, braços, pernas e pés.")]
        [SerializeField, Range(30f, 89f)] private float firstPersonMaxPitch = 55f;

        [Tooltip("Com o corpo visível: plano de corte próximo (m) em primeira pessoa, para o peito e os braços perto da câmera não serem cortados.")]
        [SerializeField, Min(0.01f)] private float firstPersonNearClip = 0.1f;

        [Header("Terceira pessoa")]
        [Tooltip("Altura do ponto que a câmera orbita, acima do pivô do Player.")]
        [SerializeField] private float pivotHeight = 1.2f;

        [SerializeField, Min(0.5f)] private float thirdPersonDistance = 4f;

        [Tooltip("Limite ao olhar para cima na terceira pessoa (valor negativo). Mais baixo que o geral: olhar muito para cima leva a câmera para baixo do chão.")]
        [SerializeField, Range(-89f, 0f)] private float thirdPersonMinPitch = -30f;

        [Tooltip("Raio da esfera usada para detectar paredes entre o Player e a câmera.")]
        [SerializeField, Min(0.05f)] private float collisionRadius = 0.25f;

        [Tooltip("Camadas que empurram a câmera para perto do Player.")]
        [SerializeField] private LayerMask collisionMask = ~0;

        private readonly RaycastHit[] hitBuffer = new RaycastHit[HitBufferSize];

        private PlayerInputReader input;
        private Renderer[] bodyRenderers;
        private CharacterController body;
        private PlayerBodyVisibility bodyVisibility;
        private PlayerMovement playerMovement;
        private Camera playerCamera;
        private float defaultNearClip;
        private CameraMode mode;
        private float yaw;
        private float pitch;

        public CameraMode Mode => mode;

        private void Awake()
        {
            if (target == null)
            {
                GameObject player = GameObject.FindGameObjectWithTag("Player");
                if (player != null) target = player.transform;
            }

            if (target == null)
            {
                Debug.LogError($"{nameof(PlayerCameraController)}: nenhum alvo definido e nenhum objeto com a tag 'Player' na cena.", this);
                enabled = false;
                return;
            }

            // Este script move o próprio transform. Se o alvo for o mesmo objeto, ele arrastaria o Player a cada frame.
            if (target == transform)
            {
                Debug.LogError($"{nameof(PlayerCameraController)} deve ficar na Main Camera, não no Player.", this);
                enabled = false;
                return;
            }

            input = target.GetComponent<PlayerInputReader>();
            bodyRenderers = target.GetComponentsInChildren<Renderer>();
            body = target.GetComponent<CharacterController>();
            bodyVisibility = target.GetComponent<PlayerBodyVisibility>();
            playerMovement = target.GetComponent<PlayerMovement>();
            playerCamera = GetComponent<Camera>();
            defaultNearClip = playerCamera.nearClipPlane;

            yaw = target.eulerAngles.y;
            SetMode(startMode);
        }

        private void OnEnable()
        {
            if (lockCursor) SetCursorLocked(true);
        }

        private void OnDisable()
        {
            if (lockCursor) SetCursorLocked(false);
        }

        private void Update()
        {
            if (input.ToggleCameraPressed)
            {
                SetMode(mode == CameraMode.FirstPerson ? CameraMode.ThirdPerson : CameraMode.FirstPerson);
            }

            // Depois de apertar Esc no Editor o cursor é liberado; um clique devolve o controle ao jogo.
            if (lockCursor && Cursor.lockState != CursorLockMode.Locked && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                SetCursorLocked(true);
            }
        }

        // LateUpdate roda depois de todos os Update: o Player já se moveu neste frame,
        // então a câmera não "treme" ao tentar seguir uma posição antiga.
        private void LateUpdate()
        {
            ApplyLookInput();

            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);

            if (mode == CameraMode.FirstPerson)
            {
                Vector3 eye = target.position + Vector3.up * eyeHeight;

                // Com o corpo visível a câmera fica nos olhos do modelo (medidos no osso da cabeça: acima e à frente do eixo do corpo)
                // mais um avanço extra ajustável; a altura fixa de antes caía na base do crânio, dentro do pescoço.
                if (ShowsBodyInFirstPerson)
                {
                    float forward = firstPersonForwardOffset;
                    if (bodyVisibility.TryGetEyes(out float eyesHeight, out float eyesForward))
                    {
                        eye = target.position + Vector3.up * eyesHeight;
                        forward += eyesForward;
                    }

                    eye += Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * forward;
                }

                transform.SetPositionAndRotation(eye, rotation);
            }
            else
            {
                Vector3 pivot = target.position + Vector3.up * pivotHeight;
                Vector3 backwards = rotation * Vector3.back;
                float distance = GetThirdPersonDistance(pivot, backwards);
                Vector3 position = KeepOutsideBody(pivot + backwards * distance, rotation);
                transform.SetPositionAndRotation(position, rotation);
            }
        }

        private void ApplyLookInput()
        {
            // Mouse e analógico só giram a câmera com o cursor travado (Esc libera); o dedo na tela funciona sempre.
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 look = input.Look;
                float scale = input.IsLookFromGamepad ? gamepadSensitivity * Time.deltaTime : mouseSensitivity;
                Rotate(look, scale);
            }

            Rotate(input.TouchLook, touchSensitivity);
        }

        private void Rotate(Vector2 look, float scale)
        {
            yaw += look.x * scale;
            // Para cima = olhar para cima = pitch diminui (no Unity, X positivo inclina para baixo).
            pitch = Mathf.Clamp(pitch - look.y * scale, GetMinPitch(), GetMaxPitch());
        }

        private bool ShowsBodyInFirstPerson => mode == CameraMode.FirstPerson && bodyVisibility != null && bodyVisibility.ShowBodyInFirstPerson;

        // Olhar para baixo: com o corpo visível em 1ª pessoa, limitado para não passar do pescoço (sem cabeça).
        private float GetMaxPitch() => ShowsBodyInFirstPerson ? Mathf.Min(maxPitch, firstPersonMaxPitch) : maxPitch;

        private float GetMinPitch() => mode == CameraMode.ThirdPerson ? Mathf.Max(minPitch, thirdPersonMinPitch) : minPitch;

        /// <summary>
        /// Lança uma esfera do pivô para trás; se bater em algo (parede, chão), aproxima a câmera
        /// para ela nunca ficar atrás de um obstáculo.
        /// </summary>
        private float GetThirdPersonDistance(Vector3 pivot, Vector3 direction)
        {
            int hits = Physics.SphereCastNonAlloc(pivot, collisionRadius, direction, hitBuffer, thirdPersonDistance, collisionMask, QueryTriggerInteraction.Ignore);
            float closest = thirdPersonDistance;

            for (int i = 0; i < hits; i++)
            {
                RaycastHit hit = hitBuffer[i];

                // O próprio Player não deve empurrar a câmera.
                if (hit.collider.transform.IsChildOf(target)) continue;

                closest = Mathf.Min(closest, hit.distance);
            }

            return Mathf.Max(closest, MinThirdPersonDistance);
        }

        /// <summary>
        /// O Player é ignorado no SphereCast acima (senão ele empurraria a câmera o tempo todo), então nada impede
        /// a câmera de ficar dentro do corpo quando um obstáculo a puxa para perto. Aqui tratamos o corpo como uma cápsula
        /// vertical (segmento + raio) e, se a câmera estiver mais perto que o raio, empurramos ela para fora.
        /// </summary>
        private Vector3 KeepOutsideBody(Vector3 position, Quaternion rotation)
        {
            if (body == null) return position;

            Vector3 center = target.TransformPoint(body.center);
            float halfSegment = Mathf.Max(0f, body.height * 0.5f - body.radius);

            // Ponto do eixo da cápsula mais próximo da câmera (o eixo do CharacterController é sempre vertical).
            Vector3 closest = new Vector3(center.x, Mathf.Clamp(position.y, center.y - halfSegment, center.y + halfSegment), center.z);

            float minDistance = body.radius + collisionRadius;
            Vector3 away = position - closest;
            float distance = away.magnitude;
            if (distance >= minDistance) return position;

            // Se a câmera estiver exatamente no eixo, não há direção "para fora": usa a de trás da câmera.
            Vector3 direction = distance > 0.0001f ? away / distance : rotation * Vector3.back;
            return closest + direction * minDistance;
        }

        private void SetMode(CameraMode newMode)
        {
            mode = newMode;

            // Ao voltar para a terceira pessoa, o ângulo pode estar além do limite dela.
            pitch = Mathf.Clamp(pitch, GetMinPitch(), GetMaxPitch());

            // Primeira pessoa: o corpo acompanha a câmera e o Player anda de frente, de ré e de lado. Terceira: o corpo vira para o movimento.
            if (playerMovement != null) playerMovement.FaceCamera = mode == CameraMode.FirstPerson;

            playerCamera.nearClipPlane = ShowsBodyInFirstPerson ? Mathf.Min(defaultNearClip, firstPersonNearClip) : defaultNearClip;

            // Com modelo de personagem: só a cabeça some (o resto do corpo continua visível e com sombra).
            if (bodyVisibility != null)
            {
                bodyVisibility.SetFirstPerson(mode == CameraMode.FirstPerson);
                return;
            }

            // Sem modelo (cápsula): na primeira pessoa a câmera fica dentro dela: escondemos o corpo,
            // mas mantemos a sombra (ShadowsOnly) para o jogador ainda "existir" no mundo.
            ShadowCastingMode bodyMode = mode == CameraMode.FirstPerson ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
            foreach (Renderer bodyRenderer in bodyRenderers)
            {
                bodyRenderer.shadowCastingMode = bodyMode;
            }
        }

        private static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
