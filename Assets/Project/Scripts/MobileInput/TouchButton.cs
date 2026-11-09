using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PROCEDURAL_GAME.MobileInput
{
    /// <summary>
    /// Botão de tela que expõe "foi apertado neste frame" e "está segurado",
    /// no mesmo estilo das Actions do Input System.
    /// </summary>
    public class TouchButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        private int pressedFrame = -1;

        /// <summary>Disparado no toque (para botões liga/desliga: não depende da ordem de execução entre scripts).</summary>
        public event Action Pressed;

        /// <summary>True enquanto o dedo estiver sobre o botão.</summary>
        public bool IsHeld { get; private set; }

        /// <summary>True apenas no frame em que o botão foi apertado.</summary>
        public bool WasPressedThisFrame => pressedFrame == Time.frameCount;

        public void OnPointerDown(PointerEventData eventData)
        {
            IsHeld = true;
            pressedFrame = Time.frameCount;
            Pressed?.Invoke();
        }

        public void OnPointerUp(PointerEventData eventData) => IsHeld = false;

        // Se o botão for desativado com o dedo em cima, o "soltar" nunca chegaria.
        private void OnDisable() => IsHeld = false;
    }
}
