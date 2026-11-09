using PROCEDURAL_GAME.MobileInput;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PROCEDURAL_GAME.Player
{
    /// <summary>
    /// Lê as Actions do Input System e os controles de toque, e expõe valores simples para o resto do jogo.
    /// Não sabe nada sobre movimento: só responde "o que o jogador está pedindo agora?".
    /// </summary>
    public class PlayerInputReader : MonoBehaviour
    {
        private const string ActionMapName = "Player";

        [Tooltip("Arquivo PlayerInputActions.inputactions (Assets/Project/Input).")]
        [SerializeField] private InputActionAsset inputActions;

        [Tooltip("Controles de toque do Canvas. Opcional: sem ele só funcionam teclado/mouse/gamepad.")]
        [SerializeField] private MobileControls mobileControls;

        private InputActionMap actionMap;
        private InputAction moveAction;
        private InputAction lookAction;
        private InputAction jumpAction;
        private InputAction sprintAction;
        private InputAction toggleCameraAction;

        /// <summary>Direção desejada (WASD / analógico esquerdo / joystick de tela), cada eixo entre -1 e 1.</summary>
        public Vector2 Move
        {
            get
            {
                Vector2 move = moveAction.ReadValue<Vector2>();
                if (mobileControls == null) return move;

                // Usa a fonte mais "inclinada", para teclado e joystick não se somarem.
                Vector2 touch = mobileControls.Move;
                return touch.sqrMagnitude > move.sqrMagnitude ? touch : move;
            }
        }

        /// <summary>Movimento da câmera (mouse / analógico direito).</summary>
        public Vector2 Look => lookAction.ReadValue<Vector2>();

        /// <summary>Arrasto do dedo neste frame, em pixels. Zero se não houver controles de toque.</summary>
        public Vector2 TouchLook => mobileControls != null ? mobileControls.Look : Vector2.zero;

        /// <summary>
        /// True se o último Look veio de um gamepad. O mouse envia "quanto moveu neste frame" (já é um delta),
        /// já o analógico envia "quão inclinado está" (precisa ser multiplicado por Time.deltaTime).
        /// </summary>
        public bool IsLookFromGamepad => lookAction.activeControl?.device is Gamepad;

        /// <summary>True apenas no frame em que o botão de alternar câmera foi apertado.</summary>
        public bool ToggleCameraPressed => toggleCameraAction.WasPressedThisFrame() || (mobileControls != null && mobileControls.ToggleCameraPressed);

        /// <summary>True apenas no frame em que o botão de pulo foi apertado.</summary>
        public bool JumpPressed => jumpAction.WasPressedThisFrame() || (mobileControls != null && mobileControls.JumpPressed);

        /// <summary>True apenas no frame em que o botão de ataque foi apertado (só na tela, por enquanto).</summary>
        public bool AttackPressed => mobileControls != null && mobileControls.AttackPressed;

        /// <summary>Correr: teclado/gamepad enquanto a tecla estiver segurada; na tela, liga/desliga pelo botão.</summary>
        public bool Sprinting => sprintAction.IsPressed() || (mobileControls != null && mobileControls.SprintOn);

        /// <summary>Desliga a corrida do botão de tela (a energia acabou). A corrida não religa sozinha: o jogador decide tocar de novo.</summary>
        public void CancelSprint()
        {
            if (mobileControls != null) mobileControls.SetSprint(false);
        }

        private void Awake()
        {
            if (inputActions == null)
            {
                Debug.LogError($"{nameof(PlayerInputReader)}: 'Input Actions' não foi atribuído no Inspector.", this);
                enabled = false;
                return;
            }

            actionMap = inputActions.FindActionMap(ActionMapName, throwIfNotFound: true);
            moveAction = actionMap.FindAction("Move", throwIfNotFound: true);
            lookAction = actionMap.FindAction("Look", throwIfNotFound: true);
            jumpAction = actionMap.FindAction("Jump", throwIfNotFound: true);
            sprintAction = actionMap.FindAction("Sprint", throwIfNotFound: true);
            toggleCameraAction = actionMap.FindAction("ToggleCamera", throwIfNotFound: true);
        }

        // As Actions só enviam valores enquanto o Action Map estiver habilitado.
        private void OnEnable() => actionMap?.Enable();

        private void OnDisable() => actionMap?.Disable();
    }
}
