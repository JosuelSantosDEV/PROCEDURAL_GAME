using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PROCEDURAL_GAME.MobileInput
{
    /// <summary>
    /// Botão de tela que expõe "foi apertado neste frame" e "está segurado",
    /// no mesmo estilo das Actions do Input System.
    /// Visual: tons de preto e cinza (<see cref="TouchControlPalette"/>); ao ser tocado pisca (acende e apaga) e afunda um pouco enquanto o
    /// dedo está em cima, para o jogador sentir que o toque pegou. Usa tempo sem escala: funciona com o jogo pausado.
    /// Espera a montagem do menu Create Mobile Controls: Image no próprio objeto (disco), filhos "Ring" (Image) e "Label" (Text).
    /// </summary>
    public class TouchButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        private const float FlashSeconds = 0.25f;
        private const float PressedScale = 0.92f;
        // Tempo (s) para afundar ao tocar e voltar ao soltar.
        private const float ScaleSeconds = 0.06f;

        private int pressedFrame = -1;
        private bool initialized;
        private Image fill;
        private Image ring;
        private Text label;
        private bool toggled;
        private float flashStart = -1f;
        private float scale = 1f;

        /// <summary>Disparado no toque (para botões liga/desliga: não depende da ordem de execução entre scripts).</summary>
        public event Action Pressed;

        /// <summary>True enquanto o dedo estiver sobre o botão.</summary>
        public bool IsHeld { get; private set; }

        /// <summary>True apenas no frame em que o botão foi apertado.</summary>
        public bool WasPressedThisFrame => pressedFrame == Time.frameCount;

        private void Awake() => Initialize();

        public void OnPointerDown(PointerEventData eventData)
        {
            IsHeld = true;
            pressedFrame = Time.frameCount;
            flashStart = Time.unscaledTime;
            Pressed?.Invoke();
        }

        public void OnPointerUp(PointerEventData eventData) => IsHeld = false;

        // Se o botão for desativado com o dedo em cima, o "soltar" nunca chegaria.
        private void OnDisable()
        {
            IsHeld = false;
            flashStart = -1f;
            scale = 1f;
            transform.localScale = Vector3.one;
            ApplyColors(0f);
        }

        /// <summary>Estado de liga/desliga (Correr): ligado fica com o disco cinza e o aro claro.</summary>
        public void SetToggled(bool on)
        {
            Initialize();
            toggled = on;
            ApplyColors(FlashAmount());
        }

        private void Update()
        {
            float target = IsHeld ? PressedScale : 1f;
            if (!Mathf.Approximately(scale, target))
            {
                scale = Mathf.MoveTowards(scale, target, (1f - PressedScale) * Time.unscaledDeltaTime / ScaleSeconds);
                transform.localScale = new Vector3(scale, scale, 1f);
            }

            if (flashStart >= 0f)
            {
                float amount = FlashAmount();
                ApplyColors(amount);
                if (amount <= 0f) flashStart = -1f;
            }
        }

        // 1 no instante do toque, cai até 0 em FlashSeconds (rápido no começo: parece um clarão).
        private float FlashAmount()
        {
            if (flashStart < 0f) return 0f;

            float t = (Time.unscaledTime - flashStart) / FlashSeconds;
            if (t >= 1f) return 0f;

            float remaining = 1f - t;
            return remaining * remaining;
        }

        private void Initialize()
        {
            if (initialized) return;
            initialized = true;

            fill = GetComponent<Image>();
            Transform ringTransform = transform.Find("Ring");
            if (ringTransform != null) ring = ringTransform.GetComponent<Image>();
            Transform labelTransform = transform.Find("Label");
            if (labelTransform != null) label = labelTransform.GetComponent<Text>();

            if (label != null) label.color = TouchControlPalette.ButtonLabel;
            ApplyColors(0f);
        }

        private void ApplyColors(float flash)
        {
            if (!initialized) return;

            Color restFill = toggled ? TouchControlPalette.ToggledFill : TouchControlPalette.ButtonFill;
            Color restRing = toggled ? TouchControlPalette.ToggledRing : TouchControlPalette.ButtonRing;

            if (fill != null) fill.color = Color.Lerp(restFill, TouchControlPalette.FlashFill, flash);
            if (ring != null) ring.color = Color.Lerp(restRing, TouchControlPalette.FlashRing, flash);
        }
    }
}
