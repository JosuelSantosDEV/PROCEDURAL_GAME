using PROCEDURAL_GAME.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PROCEDURAL_GAME.UI
{
    /// <summary>
    /// Botão "Menu" no topo da tela do jogo, com pausa e volta ao menu inicial.
    /// O botão "voltar" do Android (Esc) também abre e fecha a pausa. Montado por código, como o menu inicial.
    /// </summary>
    public class InGameMenu : MonoBehaviour
    {
        private const float ControlRowLabelWidth = 340f;

        [Tooltip("Nome da cena do menu inicial (precisa estar em File > Build Settings).")]
        [SerializeField] private string menuSceneName = "MainMenu";

        private GameObject pausePanel;

        private void Start()
        {
            if (FindObjectOfType<EventSystem>() == null)
            {
                Debug.LogWarning($"{nameof(InGameMenu)}: não há EventSystem na cena, então os botões não vão responder. Rode PROCEDURAL_GAME > Create Mobile Controls.", this);
            }

            RectTransform canvas = UiKit.CreateCanvas(transform, "InGameMenu", 100);

            // Topo central: o contador de FPS fica à esquerda e o botão de câmera à direita.
            Button open = UiKit.CreateButton(canvas, "MenuButton", "MENU", new Vector2(220f, 90f), 38, () => SetPaused(true));
            UiKit.Anchor((RectTransform)open.transform, new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(220f, 90f));

            BuildPausePanel(canvas);
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) SetPaused(!pausePanel.activeSelf);
        }

        // O tempo parado vale para o jogo todo; se este objeto for destruído com a pausa ligada, o menu inicial não pode herdá-la.
        private void OnDestroy() => Time.timeScale = 1f;

        private void BuildPausePanel(RectTransform canvas)
        {
            // Fundo escuro de tela cheia: escurece o jogo e bloqueia os toques nos controles por trás.
            Image dim = UiKit.CreateImage(canvas, "PausePanel", new Color(0f, 0f, 0f, 0.65f));
            UiKit.Stretch(dim.rectTransform);
            pausePanel = dim.gameObject;

            Image box = UiKit.CreateImage(dim.rectTransform, "Box", UiKit.PanelColor);
            UiKit.Anchor(box.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1200f, 860f));
            UiKit.AddVerticalLayout(box.gameObject, 36f, 60, TextAnchor.UpperCenter);

            Text title = UiKit.CreateText(box.rectTransform, "Title", "PAUSADO", 72, TextAnchor.MiddleCenter, UiKit.TextColor);
            title.fontStyle = FontStyle.Bold;
            UiKit.SetLayout(title.gameObject, preferredHeight: 110f);

            // Aqui dá para ajustar e testar em seguida, sem sair da partida.
            UiKit.CreateSliderRow(box.rectTransform, "Sensibilidade", ControlRowLabelWidth, ControlSettings.MaxLookSteps, ControlSettings.GetLookSteps(), ControlSettings.FormatLookSteps, ControlSettings.SetLookSteps);
            UiKit.CreateSliderRow(box.rectTransform, "Transparência", ControlRowLabelWidth, ControlSettings.MaxTransparencySteps, ControlSettings.GetTransparencySteps(), ControlSettings.FormatTransparencySteps, ControlSettings.SetTransparencySteps);

            Button resume = UiKit.CreateButton(box.rectTransform, "ResumeButton", "CONTINUAR", new Vector2(0f, 130f), 50, () => SetPaused(false));
            UiKit.SetLayout(resume.gameObject, preferredHeight: 130f);

            Button mainMenu = UiKit.CreateButton(box.rectTransform, "MainMenuButton", "MENU PRINCIPAL", new Vector2(0f, 130f), 46, GoToMainMenu);
            UiKit.SetLayout(mainMenu.gameObject, preferredHeight: 130f);

            pausePanel.SetActive(false);
        }

        private void SetPaused(bool paused)
        {
            pausePanel.SetActive(paused);
            Time.timeScale = paused ? 0f : 1f;

            if (!paused) ControlSettings.Save();
        }

        private void GoToMainMenu()
        {
            Time.timeScale = 1f;
            ControlSettings.Save();
            SceneManager.LoadScene(menuSceneName);
        }
    }
}
