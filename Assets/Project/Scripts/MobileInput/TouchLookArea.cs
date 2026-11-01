using UnityEngine;
using UnityEngine.EventSystems;

namespace PROCEDURAL_GAME.MobileInput
{
    /// <summary>
    /// Área da tela em que arrastar o dedo gira a câmera.
    /// Deve ficar atrás dos botões: toques que começam num botão não chegam aqui.
    /// </summary>
    public class TouchLookArea : MonoBehaviour, IDragHandler
    {
        private Vector2 delta;
        private int deltaFrame = -1;

        /// <summary>Quanto o dedo andou neste frame, em pixels (mesma unidade do delta do mouse).</summary>
        public Vector2 Delta => deltaFrame == Time.frameCount ? delta : Vector2.zero;

        public void OnDrag(PointerEventData eventData)
        {
            // Pode haver mais de um OnDrag no mesmo frame: acumulamos em vez de sobrescrever.
            if (deltaFrame != Time.frameCount)
            {
                delta = Vector2.zero;
                deltaFrame = Time.frameCount;
            }

            delta += eventData.delta;
        }
    }
}
