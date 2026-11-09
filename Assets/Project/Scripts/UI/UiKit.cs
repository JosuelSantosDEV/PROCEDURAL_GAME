using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace PROCEDURAL_GAME.UI
{
    /// <summary>
    /// Peças de uGUI montadas por código (painel, texto, botão, slider, lista com rolagem).
    /// Os menus são gerados a partir de dados (ex.: uma linha por categoria de desempenho), então não há prefabs
    /// para manter: para mudar cores ou tamanhos de todos os menus, edite aqui.
    /// Tamanhos em unidades do Canvas, na tela de referência 1920 x 1080 (paisagem, como os controles de toque).
    /// </summary>
    public static class UiKit
    {
        public static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);

        public static readonly Color PanelColor = new Color(0.03f, 0.04f, 0.04f, 0.90f);
        public static readonly Color ButtonColor = new Color(0.16f, 0.19f, 0.18f, 1f);
        public static readonly Color ButtonPressedColor = new Color(0.32f, 0.40f, 0.36f, 1f);
        public static readonly Color AccentColor = new Color(0.55f, 0.68f, 0.58f, 1f);
        public static readonly Color TextColor = new Color(0.90f, 0.93f, 0.91f, 1f);
        public static readonly Color MutedTextColor = new Color(0.62f, 0.67f, 0.64f, 1f);

        // Largura da alça do slider: grande o bastante para o dedo.
        private const float SliderHandleWidth = 56f;

        private static Font font;

        // LegacyRuntime é a fonte embutida do Unity 2022; evita depender do TextMesh Pro (que pede importação de recursos).
        private static Font DefaultFont
        {
            get
            {
                if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return font;
            }
        }

        public static RectTransform CreateCanvas(Transform parent, string name, int sortingOrder)
        {
            GameObject go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);

            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            return (RectTransform)go.transform;
        }

        public static RectTransform CreateRect(Transform parent, string name)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>Retângulo de tamanho fixo ancorado: <paramref name="anchor"/> vale para âncora e pivô (ex.: (0.5, 1) = topo central).</summary>
        public static void Anchor(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        public static Image CreateImage(Transform parent, string name, Color color)
        {
            Image image = CreateRect(parent, name).gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        public static Text CreateText(Transform parent, string name, string content, int fontSize, TextAnchor alignment, Color color)
        {
            Text text = CreateRect(parent, name).gameObject.AddComponent<Text>();
            text.font = DefaultFont;
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        public static Button CreateButton(Transform parent, string name, string label, Vector2 size, int fontSize, UnityAction onClick)
        {
            RectTransform rect = CreateRect(parent, name);
            rect.sizeDelta = size;

            // A cor vem do ColorBlock do botão; a imagem fica branca para a tinta não se acumular.
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = Color.white;

            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            ColorBlock colors = button.colors;
            colors.normalColor = ButtonColor;
            colors.highlightedColor = Color.Lerp(ButtonColor, ButtonPressedColor, 0.4f);
            colors.selectedColor = ButtonColor;
            colors.pressedColor = ButtonPressedColor;
            colors.disabledColor = new Color(ButtonColor.r, ButtonColor.g, ButtonColor.b, 0.4f);
            button.colors = colors;

            LayoutElement layout = rect.gameObject.AddComponent<LayoutElement>();
            layout.preferredWidth = size.x;
            layout.preferredHeight = size.y;

            Text text = CreateText(rect, "Label", label, fontSize, TextAnchor.MiddleCenter, TextColor);
            Stretch(text.rectTransform);

            if (onClick != null) button.onClick.AddListener(onClick);
            return button;
        }

        /// <summary>Slider que vai de <paramref name="min"/> a <paramref name="max"/> em passos inteiros (cabe bem em toque).</summary>
        public static Slider CreateSlider(Transform parent, string name, int min, int max, int value, UnityAction<float> onChanged)
        {
            RectTransform root = CreateRect(parent, name);
            Slider slider = root.gameObject.AddComponent<Slider>();

            LayoutElement layout = root.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = 80f;
            layout.flexibleWidth = 1f;

            Image background = CreateImage(root, "Background", ButtonColor);
            SetBand(background.rectTransform, 0.35f, 0.65f, 0f);

            RectTransform fillArea = CreateRect(root, "Fill Area");
            SetBand(fillArea, 0.35f, 0.65f, SliderHandleWidth * 0.5f);

            // A área da alça tem a mesma folga lateral da área de preenchimento: o fim da barra fica sob o centro da alça.
            Image fill = CreateImage(fillArea, "Fill", AccentColor);
            Stretch(fill.rectTransform);

            RectTransform handleArea = CreateRect(root, "Handle Slide Area");
            SetBand(handleArea, 0f, 1f, SliderHandleWidth * 0.5f);

            // O Slider só move a âncora horizontal da alça; na vertical ela precisa já ocupar a altura toda.
            Image handle = CreateImage(handleArea, "Handle", TextColor);
            handle.rectTransform.anchorMin = new Vector2(0f, 0f);
            handle.rectTransform.anchorMax = new Vector2(0f, 1f);
            handle.rectTransform.sizeDelta = new Vector2(SliderHandleWidth, 0f);

            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = true;
            slider.SetValueWithoutNotify(value);

            if (onChanged != null) slider.onValueChanged.AddListener(onChanged);
            return slider;
        }

        /// <summary>Linha "rótulo — slider — valor". <paramref name="onChanged"/> recebe o passo escolhido (0 a <paramref name="maxSteps"/>).</summary>
        public static void CreateSliderRow(Transform parent, string labelText, float labelWidth, int maxSteps, int currentSteps, System.Func<int, string> format, System.Action<int> onChanged)
        {
            RectTransform row = CreateRect(parent, "SliderRow");
            AddHorizontalLayout(row.gameObject, 30f, TextAnchor.MiddleCenter);
            SetLayout(row.gameObject, preferredHeight: 100f);

            Text label = CreateText(row, "Label", labelText, 42, TextAnchor.MiddleLeft, TextColor);
            SetLayout(label.gameObject, preferredWidth: labelWidth, preferredHeight: 100f);

            Text value = CreateText(row, "Value", format(currentSteps), 40, TextAnchor.MiddleRight, AccentColor);
            SetLayout(value.gameObject, preferredWidth: 230f, preferredHeight: 100f);

            CreateSlider(row, "Slider", 0, maxSteps, currentSteps, newValue =>
            {
                int steps = Mathf.RoundToInt(newValue);
                onChanged(steps);
                value.text = format(steps);
            });

            // Ordem visual: rótulo, slider, valor. O valor foi criado antes só para o slider poder atualizá-lo.
            value.transform.SetAsLastSibling();
        }

        /// <summary>Lista vertical com rolagem por toque. Os itens entram em <paramref name="content"/>, que cresce sozinho.</summary>
        public static RectTransform CreateScrollView(Transform parent, string name, out RectTransform content)
        {
            RectTransform root = CreateRect(parent, name);

            // O fundo também recebe o toque de arrastar: sem uma imagem sob o dedo a lista não rola.
            Image background = root.gameObject.AddComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.25f);

            ScrollRect scroll = root.gameObject.AddComponent<ScrollRect>();

            RectTransform viewport = CreateRect(root, "Viewport");
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();

            content = CreateRect(viewport, "Content");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;

            VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 20f;
            layout.padding = new RectOffset(30, 30, 30, 30);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = 40f;

            return root;
        }

        public static VerticalLayoutGroup AddVerticalLayout(GameObject target, float spacing, int padding, TextAnchor alignment)
        {
            VerticalLayoutGroup layout = target.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = new RectOffset(padding, padding, padding, padding);
            layout.childAlignment = alignment;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return layout;
        }

        public static HorizontalLayoutGroup AddHorizontalLayout(GameObject target, float spacing, TextAnchor alignment)
        {
            HorizontalLayoutGroup layout = target.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = alignment;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            return layout;
        }

        public static LayoutElement SetLayout(GameObject target, float preferredWidth = -1f, float preferredHeight = -1f, float flexibleWidth = -1f, float flexibleHeight = -1f)
        {
            LayoutElement layout = target.GetComponent<LayoutElement>();
            if (layout == null) layout = target.AddComponent<LayoutElement>();

            layout.preferredWidth = preferredWidth;
            layout.preferredHeight = preferredHeight;
            layout.flexibleWidth = flexibleWidth;
            layout.flexibleHeight = flexibleHeight;
            return layout;
        }

        // Faixa horizontal entre duas alturas relativas (0..1), com folga nas laterais.
        private static void SetBand(RectTransform rect, float minY, float maxY, float sideMargin)
        {
            rect.anchorMin = new Vector2(0f, minY);
            rect.anchorMax = new Vector2(1f, maxY);
            rect.offsetMin = new Vector2(sideMargin, 0f);
            rect.offsetMax = new Vector2(-sideMargin, 0f);
        }
    }
}
