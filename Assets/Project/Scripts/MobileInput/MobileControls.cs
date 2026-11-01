using PinePie.SimpleJoystick;
using UnityEngine;

namespace PROCEDURAL_GAME.MobileInput
{
    /// <summary>
    /// Reúne os controles de toque da tela (joystick, arrastar para olhar e botões).
    /// Fica no Canvas; o <c>PlayerInputReader</c> lê daqui junto com teclado/mouse/gamepad.
    /// Para um novo botão (ex.: ataque), adicione um <see cref="TouchButton"/> aqui.
    /// </summary>
    public class MobileControls : MonoBehaviour
    {
        [SerializeField] private JoystickController joystick;
        [SerializeField] private TouchLookArea lookArea;
        [SerializeField] private TouchButton jumpButton;
        [SerializeField] private TouchButton sprintButton;
        [SerializeField] private TouchButton toggleCameraButton;

        /// <summary>Direção do joystick, cada eixo entre -1 e 1.</summary>
        public Vector2 Move => joystick != null ? joystick.InputDirection : Vector2.zero;

        /// <summary>Arrasto do dedo neste frame, em pixels.</summary>
        public Vector2 Look => lookArea != null ? lookArea.Delta : Vector2.zero;

        public bool JumpPressed => jumpButton != null && jumpButton.WasPressedThisFrame;

        public bool SprintHeld => sprintButton != null && sprintButton.IsHeld;

        public bool ToggleCameraPressed => toggleCameraButton != null && toggleCameraButton.WasPressedThisFrame;
    }
}
