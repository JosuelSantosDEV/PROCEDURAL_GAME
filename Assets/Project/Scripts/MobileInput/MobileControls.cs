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
    /// </summary>
    public class MobileControls : MonoBehaviour
    {
        private static readonly Color SprintOnColor = new Color(1f, 0.6f, 0.1f, 0.75f);

        [SerializeField] private JoystickController joystick;
        [SerializeField] private TouchLookArea lookArea;
        [SerializeField] private TouchButton jumpButton;
        [SerializeField] private TouchButton sprintButton;
        [SerializeField] private TouchButton toggleCameraButton;
        [SerializeField] private TouchButton attackButton;

        private CanvasGroup canvasGroup;
        private Image sprintFill;
        private Color sprintOffColor;

        /// <summary>Direção do joystick, cada eixo entre -1 e 1.</summary>
        public Vector2 Move => joystick != null ? joystick.InputDirection : Vector2.zero;

        /// <summary>Arrasto do dedo neste frame, em pixels.</summary>
        public Vector2 Look => lookArea != null ? lookArea.Delta : Vector2.zero;

        public bool JumpPressed => jumpButton != null && jumpButton.WasPressedThisFrame;

        public bool AttackPressed => attackButton != null && attackButton.WasPressedThisFrame;

        /// <summary>Corrida ligada pelo botão: vale até o próximo toque, mesmo com a manete solta.</summary>
        public bool SprintOn { get; private set; }

        public bool ToggleCameraPressed => toggleCameraButton != null && toggleCameraButton.WasPressedThisFrame;

        private void Awake()
        {
            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();

            if (sprintButton == null) return;

            sprintFill = sprintButton.GetComponent<Image>();
            if (sprintFill != null) sprintOffColor = sprintFill.color;
        }

        private void OnEnable()
        {
            if (sprintButton != null) sprintButton.Pressed += ToggleSprint;
        }

        private void OnDisable()
        {
            if (sprintButton != null) sprintButton.Pressed -= ToggleSprint;
        }

        // Confere todo frame: a transparência pode mudar no menu de pausa.
        private void Update()
        {
            float opacity = ControlSettings.ControlsOpacity;
            if (!Mathf.Approximately(canvasGroup.alpha, opacity)) canvasGroup.alpha = opacity;
        }

        private void ToggleSprint() => SetSprint(!SprintOn);

        /// <summary>Liga ou desliga a corrida, com a cor do botão junto.</summary>
        public void SetSprint(bool on)
        {
            SprintOn = on;
            if (sprintFill != null) sprintFill.color = on ? SprintOnColor : sprintOffColor;
        }
    }
}
