using PROCEDURAL_GAME.MobileInput;
using PROCEDURAL_GAME.Player;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PROCEDURAL_GAME.UI
{
    /// <summary>
    /// Tela da sobrevivência, montada por código como os outros menus: barras de vida e energia, ícones de sede e fome (o toque mostra o nome;
    /// na primeira aparição o nome surge sozinho), botão de interação só quando há o que fazer (BEBER, COMER) e a tela de morte, depois da animação.
    /// Lê o <see cref="PlayerStats"/> e o <see cref="PlayerActions"/>; não decide nada.
    /// Os ícones são letras provisórias até haver arte.
    /// </summary>
    [RequireComponent(typeof(PlayerStats), typeof(PlayerActions))]
    public class SurvivalHud : MonoBehaviour
    {
        private const int CanvasOrder = 50;      // acima dos controles de toque, abaixo do menu de pausa (100)
        private const int DeathCanvasOrder = 150; // acima de tudo: bloqueia os toques em MENU e nos controles
        private const float MessageSeconds = 2.5f;
        private const float DeathScreenDelayAfterClip = 0.4f;

        private static readonly Color BarBackground = new Color(0f, 0f, 0f, 0.55f);
        private static readonly Color LifeColor = new Color(0.72f, 0.16f, 0.14f, 1f);
        private static readonly Color EnergyColor = new Color(0.85f, 0.62f, 0.15f, 1f);
        private static readonly Color ThirstColor = new Color(0.16f, 0.42f, 0.70f, 1f);
        private static readonly Color HungerColor = new Color(0.72f, 0.40f, 0.12f, 1f);

        [Tooltip("Nome da cena do menu inicial (precisa estar em File > Build Settings).")]
        [SerializeField] private string menuSceneName = "MainMenu";

        private PlayerStats stats;
        private PlayerActions actions;
        private RectTransform lifeFill;
        private RectTransform energyFill;
        private GameObject thirstIcon;
        private GameObject hungerIcon;
        private GameObject interactButton;
        private Text interactLabel;
        private PlayerActions.Interaction shownInteraction;
        private Text message;
        private float messageUntil;
        private float deathScreenAt = -1f;

        private void Start()
        {
            stats = GetComponent<PlayerStats>();
            stats.Died += OnDied;

            actions = GetComponent<PlayerActions>();
            actions.Message += ShowMessage;

            RectTransform canvas = UiKit.CreateCanvas(null, "SurvivalHud", CanvasOrder);

            // Topo, à direita do botão MENU: à esquerda ficam o contador de FPS e o botão DEV, e no canto o botão de câmera.
            lifeFill = CreateBar(canvas, "LifeBar", "Vida", new Vector2(440f, -30f), LifeColor);
            energyFill = CreateBar(canvas, "EnergyBar", "Energia", new Vector2(440f, -74f), EnergyColor);

            thirstIcon = CreateIcon(canvas, "ThirstIcon", "S", new Vector2(275f, -118f), ThirstColor, () => ShowMessage("Sede"));
            hungerIcon = CreateIcon(canvas, "HungerIcon", "F", new Vector2(375f, -118f), HungerColor, () => ShowMessage("Fome"));

            message = UiKit.CreateText(canvas, "Message", string.Empty, 40, TextAnchor.MiddleLeft, UiKit.TextColor);
            UiKit.Anchor(message.rectTransform, new Vector2(0.5f, 1f), new Vector2(620f, -118f), new Vector2(340f, 90f));

            // Acima do botão Pular, onde o polegar direito já está.
            Button interact = UiKit.CreateButton(canvas, "InteractButton", string.Empty, new Vector2(220f, 110f), 38, actions.Interact);
            UiKit.Anchor((RectTransform)interact.transform, new Vector2(1f, 0f), new Vector2(-90f, 320f), new Vector2(220f, 110f));
            interactButton = interact.gameObject;
            interactLabel = interact.GetComponentInChildren<Text>();
            StyleAsTouchControl(interact);
            interactButton.SetActive(false);
        }

        // Mesmo visual dos controles de toque (preto e cinza) e o mesmo clarão ao tocar. No celular o estado "destacado" ficaria preso
        // depois do toque: fica igual ao normal.
        private static void StyleAsTouchControl(Button button)
        {
            ColorBlock colors = button.colors;
            colors.normalColor = TouchControlPalette.ButtonFill;
            colors.highlightedColor = TouchControlPalette.ButtonFill;
            colors.selectedColor = TouchControlPalette.ButtonFill;
            colors.pressedColor = TouchControlPalette.FlashFill;
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            Text label = button.GetComponentInChildren<Text>();
            if (label != null) label.color = TouchControlPalette.ButtonLabel;
        }

        private void OnDestroy()
        {
            if (stats != null) stats.Died -= OnDied;
            if (actions != null) actions.Message -= ShowMessage;
        }

        private void Update()
        {
            SetFill(lifeFill, stats.Life);
            SetFill(energyFill, stats.Energy);

            ShowIcon(thirstIcon, stats.IsThirsty, "Sede");
            ShowIcon(hungerIcon, stats.IsHungry, "Fome");

            ShowInteraction(actions.Available);

            if (deathScreenAt > 0f && Time.unscaledTime >= deathScreenAt)
            {
                deathScreenAt = -1f;
                ShowDeathScreen();
            }

            if (message.text.Length > 0 && Time.unscaledTime >= messageUntil) message.text = string.Empty;
        }

        private void ShowInteraction(PlayerActions.Interaction interaction)
        {
            if (interaction == shownInteraction) return;

            shownInteraction = interaction;
            interactButton.SetActive(interaction != PlayerActions.Interaction.None);
            interactLabel.text = interaction == PlayerActions.Interaction.Eat ? "COMER" : "BEBER";
        }

        private void ShowMessage(string text)
        {
            message.text = text;
            messageUntil = Time.unscaledTime + MessageSeconds;
        }

        // Ao aparecer, o nome surge sozinho: assim ninguém precisa descobrir que o ícone é tocável.
        private void ShowIcon(GameObject icon, bool active, string text)
        {
            if (icon.activeSelf == active) return;

            icon.SetActive(active);
            if (active) ShowMessage(text);
        }

        private static void SetFill(RectTransform fill, float value)
        {
            if (!Mathf.Approximately(fill.anchorMax.x, value)) fill.anchorMax = new Vector2(value, 1f);
        }

        private static RectTransform CreateBar(RectTransform canvas, string name, string label, Vector2 position, Color color)
        {
            Image background = UiKit.CreateImage(canvas, name, BarBackground);
            background.raycastTarget = false; // o toque passa: em cima da barra também dá para girar a câmera
            UiKit.Anchor(background.rectTransform, new Vector2(0.5f, 1f), position, new Vector2(420f, 34f));

            Image fill = UiKit.CreateImage(background.rectTransform, "Fill", color);
            fill.raycastTarget = false;
            UiKit.Stretch(fill.rectTransform);

            Text text = UiKit.CreateText(background.rectTransform, "Label", label, 24, TextAnchor.MiddleCenter, UiKit.TextColor);
            UiKit.Stretch(text.rectTransform);

            return fill.rectTransform;
        }

        private static GameObject CreateIcon(RectTransform canvas, string name, string letter, Vector2 position, Color color, UnityEngine.Events.UnityAction onTap)
        {
            Button icon = UiKit.CreateButton(canvas, name, letter, new Vector2(90f, 90f), 48, onTap);
            UiKit.Anchor((RectTransform)icon.transform, new Vector2(0.5f, 1f), position, new Vector2(90f, 90f));

            ColorBlock colors = icon.colors;
            colors.normalColor = color;
            colors.highlightedColor = color;
            colors.selectedColor = color;
            icon.colors = colors;

            icon.gameObject.SetActive(false);
            return icon.gameObject;
        }

        // A tela de morte só entra depois da animação. O Esc/voltar da pausa devolveria o tempo por baixo dela.
        private void OnDied()
        {
            deathScreenAt = Time.unscaledTime + actions.DeathSeconds + DeathScreenDelayAfterClip;

            InGameMenu pauseMenu = FindObjectOfType<InGameMenu>();
            if (pauseMenu != null) pauseMenu.enabled = false;
        }

        // Morte: por enquanto perde tudo e recomeça a partida (decisão do usuário; gravar progresso e continuar por anúncio vêm depois).
        private void ShowDeathScreen()
        {
            Time.timeScale = 0f;

            RectTransform canvas = UiKit.CreateCanvas(null, "DeathScreen", DeathCanvasOrder);

            Image dim = UiKit.CreateImage(canvas, "Dim", new Color(0f, 0f, 0f, 0.8f));
            UiKit.Stretch(dim.rectTransform);

            Image box = UiKit.CreateImage(dim.rectTransform, "Box", UiKit.PanelColor);
            UiKit.Anchor(box.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(800f, 560f));
            UiKit.AddVerticalLayout(box.gameObject, 36f, 60, TextAnchor.UpperCenter);

            Text title = UiKit.CreateText(box.rectTransform, "Title", "VOCÊ MORREU", 72, TextAnchor.MiddleCenter, UiKit.TextColor);
            title.fontStyle = FontStyle.Bold;
            UiKit.SetLayout(title.gameObject, preferredHeight: 110f);

            Button restart = UiKit.CreateButton(box.rectTransform, "RestartButton", "RECOMEÇAR", new Vector2(0f, 130f), 50, Restart);
            UiKit.SetLayout(restart.gameObject, preferredHeight: 130f);

            Button menu = UiKit.CreateButton(box.rectTransform, "MainMenuButton", "MENU PRINCIPAL", new Vector2(0f, 130f), 46, GoToMainMenu);
            UiKit.SetLayout(menu.gameObject, preferredHeight: 130f);
        }

        private static void Restart()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        private void GoToMainMenu()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(menuSceneName);
        }
    }
}
