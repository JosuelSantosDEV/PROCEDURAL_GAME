using PinePie.SimpleJoystick;
using PROCEDURAL_GAME.Core;
using UnityEngine;
using UnityEngine.UI;

namespace PROCEDURAL_GAME.MobileInput
{
    /// <summary>
    /// Reúne os controles de toque da tela (joystick, arrastar para olhar e botões).
    /// Fica no Canvas; o <c>PlayerInputReader</c> lê daqui junto com teclado/mouse/gamepad.
    /// Para um novo botão (ex.: ataque), adicione um <see cref="TouchButton"/> aqui.
    /// O botão de correr é liga/desliga: um toque liga, outro desliga, e o dedo fica livre para girar a câmera.
    /// Visual em preto e cinza (<see cref="TouchControlPalette"/>): os botões piscam sozinhos ao toque (<see cref="TouchButton"/>); o
    /// joystick clareia enquanto o dedo está nele.
    /// </summary>
    public class MobileControls : MonoBehaviour
    {
        // Tempo (s) para o joystick clarear ao tocar / voltar ao soltar.
        private const float JoystickFadeSeconds = 0.12f;

        [SerializeField] private JoystickController joystick;
        [SerializeField] private TouchLookArea lookArea;
        [SerializeField] private TouchButton jumpButton;
        [SerializeField] private TouchButton sprintButton;
        [SerializeField] private TouchButton toggleCameraButton;
        [SerializeField] private TouchButton attackButton;
        [SerializeField] private TouchButton flashlightButton;

        private CanvasGroup canvasGroup;
        private Image joystickBase;
        private Image joystickHandle;
        private bool joystickTouched;
        private float joystickGlow;

        /// <summary>Direção do joystick, cada eixo entre -1 e 1.</summary>
        public Vector2 Move => joystick != null ? joystick.InputDirection : Vector2.zero;

        /// <summary>Arrasto do dedo neste frame, em pixels.</summary>
        public Vector2 Look => lookArea != null ? lookArea.Delta : Vector2.zero;

        public bool JumpPressed => jumpButton != null && jumpButton.WasPressedThisFrame;

        public bool JumpHeld => jumpButton != null && jumpButton.IsHeld;

        public bool AttackPressed => attackButton != null && attackButton.WasPressedThisFrame;

        /// <summary>Corrida ligada pelo botão: vale até o próximo toque, mesmo com a manete solta.</summary>
        public bool SprintOn { get; private set; }

        public bool ToggleCameraPressed => toggleCameraButton != null && toggleCameraButton.WasPressedThisFrame;

        public bool FlashlightPressed => flashlightButton != null && flashlightButton.WasPressedThisFrame;

        /// <summary>Botão da lanterna aceso (ligada) ou não.</summary>
        public void SetFlashlight(bool on)
        {
            if (flashlightButton != null) flashlightButton.SetToggled(on);
        }

        private void Awake()
        {
            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();

            if (joystick != null)
            {
                if (joystick.joystickBase != null) joystickBase = joystick.joystickBase.GetComponent<Image>();
                if (joystick.handle != null) joystickHandle = joystick.handle.GetComponent<Image>();
            }

            ApplyJoystickColors();
        }

        private void OnEnable()
        {
            if (sprintButton != null) sprintButton.Pressed += ToggleSprint;

            if (joystick != null)
            {
                joystick.OnTouchPressed += OnJoystickPressed;
                joystick.OnTouchRemoved += OnJoystickRemoved;
            }
        }

        private void OnDisable()
        {
            if (sprintButton != null) sprintButton.Pressed -= ToggleSprint;

            if (joystick != null)
            {
                joystick.OnTouchPressed -= OnJoystickPressed;
                joystick.OnTouchRemoved -= OnJoystickRemoved;
            }

            // O "soltar" pode não chegar com o objeto desligado.
            joystickTouched = false;
            joystickGlow = 0f;
            ApplyJoystickColors();
        }

        // Confere todo frame: a transparência pode mudar no menu de pausa.
        private void Update()
        {
            float opacity = ControlSettings.ControlsOpacity;
            if (!Mathf.Approximately(canvasGroup.alpha, opacity)) canvasGroup.alpha = opacity;

            float target = joystickTouched ? 1f : 0f;
            if (!Mathf.Approximately(joystickGlow, target))
            {
                joystickGlow = Mathf.MoveTowards(joystickGlow, target, Time.unscaledDeltaTime / JoystickFadeSeconds);
                ApplyJoystickColors();
            }
        }

        private void OnJoystickPressed() => joystickTouched = true;

        private void OnJoystickRemoved() => joystickTouched = false;

        private void ApplyJoystickColors()
        {
            if (joystickBase != null) joystickBase.color = Color.Lerp(TouchControlPalette.JoystickBase, TouchControlPalette.JoystickBaseActive, joystickGlow);
            if (joystickHandle != null) joystickHandle.color = Color.Lerp(TouchControlPalette.JoystickHandle, TouchControlPalette.JoystickHandleActive, joystickGlow);
        }

        private void ToggleSprint() => SetSprint(!SprintOn);

        /// <summary>Nome do botão de pulo (Pular, Mergulhar, Subir) e se fica apagado (sem efeito agora).</summary>
        public void SetJumpButton(string label, bool dimmed)
        {
            if (jumpButton == null) return;
            jumpButton.SetLabel(label);
            jumpButton.SetDimmed(dimmed);
        }

        /// <summary>Liga ou desliga a corrida, com o visual do botão junto.</summary>
        public void SetSprint(bool on)
        {
            SprintOn = on;
            if (sprintButton != null) sprintButton.SetToggled(on);
        }
    }
}
