using System.Collections.Generic;
using PROCEDURAL_GAME.Core;
using PROCEDURAL_GAME.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PROCEDURAL_GAME.UI
{
    /// <summary>
    /// Menu inicial: escolher o bioma, jogar e ajustar o desempenho.
    /// A tela é montada por código (<see cref="UiKit"/>) a partir dos dados:
    /// - a lista de biomas vem do campo <c>Biomes</c>;
    /// - cada categoria de objeto (campo 'Category' dos Scatter Profiles do bioma escolhido) ganha um slider de densidade.
    /// Um tipo novo de objeto (ex.: grama) aparece aqui sem mexer neste script: basta dar uma categoria ao perfil.
    /// </summary>
    public class MainMenuController : MonoBehaviour
    {
        private const string LastBiomeKey = "menu.biome";

        // O slider vai de 0 a 40 em passos de 1 = 5% de densidade (0% a 200%).
        private const int StepsPerUnit = 20;
        private const int PercentPerStep = 100 / StepsPerUnit;
        private const int MaxSteps = (int)(PerformanceSettings.MaxDensity * StepsPerUnit);

        // Passos do slider de FPS alvo: 30, 35 ... 120.
        private const int MaxFpsSteps = (PerformanceSettings.MaxTargetFps - PerformanceSettings.MinTargetFps) / PerformanceSettings.TargetFpsStep;

        private const float RowLabelWidth = 380f;

        [Tooltip("Biomas oferecidos, na ordem em que aparecem.")]
        [SerializeField] private BiomeProfile[] biomes;

        [Tooltip("Nome da cena do jogo (precisa estar em File > Build Settings).")]
        [SerializeField] private string gameSceneName = "GameWorld";

        [Tooltip("Câmera do menu: a cor do fundo acompanha a neblina do bioma escolhido.")]
        [SerializeField] private Camera menuCamera;

        private readonly List<string> categories = new List<string>();

        private int biomeIndex;
        private GameObject mainPanel;
        private GameObject performancePanel;
        private Text biomeNameText;
        private Text biomeDescriptionText;
        private Text performanceTitleText;
        private RectTransform performanceContent;

        private BiomeProfile CurrentBiome => biomes[biomeIndex];

        private void Start()
        {
            if (biomes == null || biomes.Length == 0)
            {
                Debug.LogError($"{nameof(MainMenuController)}: nenhum bioma configurado. Rode PROCEDURAL_GAME > Create Main Menu.", this);
                return;
            }

            // Ao voltar de uma pausa, o jogo deixa o tempo parado.
            Time.timeScale = 1f;

            biomeIndex = FindSavedBiomeIndex();
            BuildUi();
            ShowMain();
            RefreshBiome();
        }

        private void Update()
        {
            // Botão "voltar" do Android chega como Esc.
            bool back = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
            if (back && performancePanel != null && performancePanel.activeSelf) ShowMain();
        }

        private int FindSavedBiomeIndex()
        {
            string saved = PlayerPrefs.GetString(LastBiomeKey, string.Empty);

            for (int i = 0; i < biomes.Length; i++)
            {
                if (biomes[i] != null && biomes[i].name == saved) return i;
            }

            return 0;
        }

        private void BuildUi()
        {
            RectTransform canvas = UiKit.CreateCanvas(transform, "MenuCanvas", 0);
            BuildMainPanel(canvas);
            BuildPerformancePanel(canvas);
        }

        private void BuildMainPanel(RectTransform canvas)
        {
            Image panel = UiKit.CreateImage(canvas, "MainPanel", UiKit.PanelColor);
            mainPanel = panel.gameObject;
            UiKit.Anchor(panel.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000f, 900f));
            UiKit.AddVerticalLayout(mainPanel, 36f, 60, TextAnchor.UpperCenter);

            Text title = UiKit.CreateText(panel.rectTransform, "Title", "PROCEDURAL GAME", 84, TextAnchor.MiddleCenter, UiKit.TextColor);
            title.fontStyle = FontStyle.Bold;
            UiKit.SetLayout(title.gameObject, preferredHeight: 130f);

            // Seletor de bioma: [<]  nome + descrição  [>]
            RectTransform selector = UiKit.CreateRect(panel.rectTransform, "BiomeSelector");
            UiKit.AddHorizontalLayout(selector.gameObject, 24f, TextAnchor.MiddleCenter);
            UiKit.SetLayout(selector.gameObject, preferredHeight: 240f);

            UiKit.CreateButton(selector, "PreviousBiome", "<", new Vector2(130f, 130f), 64, () => ChangeBiome(-1));

            RectTransform info = UiKit.CreateRect(selector, "BiomeInfo");
            UiKit.AddVerticalLayout(info.gameObject, 12f, 0, TextAnchor.MiddleCenter);
            UiKit.SetLayout(info.gameObject, flexibleWidth: 1f);

            biomeNameText = UiKit.CreateText(info, "BiomeName", string.Empty, 60, TextAnchor.MiddleCenter, UiKit.TextColor);
            biomeNameText.fontStyle = FontStyle.Bold;
            biomeDescriptionText = UiKit.CreateText(info, "BiomeDescription", string.Empty, 32, TextAnchor.UpperCenter, UiKit.MutedTextColor);

            UiKit.CreateButton(selector, "NextBiome", ">", new Vector2(130f, 130f), 64, () => ChangeBiome(1));

            Button play = UiKit.CreateButton(panel.rectTransform, "PlayButton", "JOGAR", new Vector2(0f, 150f), 60, Play);
            ColorBlock colors = play.colors;
            colors.normalColor = Darken(UiKit.AccentColor, 0.6f);
            play.colors = colors;
            UiKit.SetLayout(play.gameObject, preferredHeight: 150f);

            Button performance = UiKit.CreateButton(panel.rectTransform, "PerformanceButton", "DESEMPENHO", new Vector2(0f, 120f), 46, ShowPerformance);
            UiKit.SetLayout(performance.gameObject, preferredHeight: 120f);
        }

        private void BuildPerformancePanel(RectTransform canvas)
        {
            Image panel = UiKit.CreateImage(canvas, "PerformancePanel", UiKit.PanelColor);
            performancePanel = panel.gameObject;
            UiKit.Anchor(panel.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1300f, 1000f));
            UiKit.AddVerticalLayout(performancePanel, 30f, 50, TextAnchor.UpperCenter);

            performanceTitleText = UiKit.CreateText(panel.rectTransform, "Title", "Desempenho", 56, TextAnchor.MiddleCenter, UiKit.TextColor);
            performanceTitleText.fontStyle = FontStyle.Bold;
            UiKit.SetLayout(performanceTitleText.gameObject, preferredHeight: 90f);

            Text hint = UiKit.CreateText(panel.rectTransform, "Hint", "Menos objetos e menos FPS alvo = menos calor e bateria; mais FPS exige mais do aparelho.", 30, TextAnchor.MiddleCenter, UiKit.MutedTextColor);
            UiKit.SetLayout(hint.gameObject, preferredHeight: 50f);

            RectTransform scroll = UiKit.CreateScrollView(panel.rectTransform, "Options", out performanceContent);
            UiKit.SetLayout(scroll.gameObject, flexibleHeight: 1f);

            RectTransform footer = UiKit.CreateRect(panel.rectTransform, "Footer");
            UiKit.AddHorizontalLayout(footer.gameObject, 30f, TextAnchor.MiddleCenter);
            UiKit.SetLayout(footer.gameObject, preferredHeight: 120f);

            UiKit.CreateButton(footer, "ResetButton", "RESTAURAR PADRÃO", new Vector2(520f, 110f), 38, ResetPerformance);
            UiKit.CreateButton(footer, "BackButton", "VOLTAR", new Vector2(520f, 110f), 44, ShowMain);
        }

        private void ChangeBiome(int direction)
        {
            biomeIndex = (biomeIndex + direction + biomes.Length) % biomes.Length;
            RefreshBiome();
        }

        private void RefreshBiome()
        {
            BiomeProfile biome = CurrentBiome;
            biomeNameText.text = biome.DisplayName;
            biomeDescriptionText.text = biome.description;

            // Fundo na cor da neblina do bioma, escurecida para o texto claro continuar legível.
            if (menuCamera != null)
            {
                menuCamera.clearFlags = CameraClearFlags.SolidColor;
                menuCamera.backgroundColor = Darken(biome.fogColor, 0.55f);
            }
        }

        private void ShowMain()
        {
            // Sair da tela de opções grava os valores (PlayerPrefs só vai ao disco ao salvar).
            PerformanceSettings.Save();

            performancePanel.SetActive(false);
            mainPanel.SetActive(true);
        }

        private void ShowPerformance()
        {
            mainPanel.SetActive(false);
            performancePanel.SetActive(true);

            performanceTitleText.text = $"Desempenho — {CurrentBiome.DisplayName}";
            RebuildRows();
        }

        private void Play()
        {
            GameSession.SelectedBiome = CurrentBiome;
            PlayerPrefs.SetString(LastBiomeKey, CurrentBiome.name);
            PerformanceSettings.Save();

            SceneManager.LoadScene(gameSceneName);
        }

        private void ResetPerformance()
        {
            foreach (string category in categories) PerformanceSettings.SetDensity(category, PerformanceSettings.DefaultDensity);
            PerformanceSettings.ResetGeneral();
            RebuildRows();
        }

        /// <summary>
        /// Opções gerais (FPS) e, abaixo, uma linha por categoria de objeto do bioma escolhido, lida dos próprios perfis.
        /// </summary>
        private void RebuildRows()
        {
            foreach (Transform child in performanceContent)
            {
                // Desativar antes: o Destroy só age no fim do frame e a linha antiga ainda entraria no layout.
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }

            CreateSectionTitle("Geral");
            CreateTargetFpsRow();
            CreateFpsCounterRow();
            CreateSectionTitle("Objetos");

            categories.Clear();
            if (CurrentBiome.scatter != null)
            {
                foreach (ScatterProfile profile in CurrentBiome.scatter)
                {
                    if (profile != null && !string.IsNullOrEmpty(profile.category) && !categories.Contains(profile.category))
                    {
                        categories.Add(profile.category);
                    }
                }
            }

            if (categories.Count == 0)
            {
                Text empty = UiKit.CreateText(performanceContent, "Empty", "Este bioma não tem objetos configuráveis.", 36, TextAnchor.MiddleCenter, UiKit.MutedTextColor);
                UiKit.SetLayout(empty.gameObject, preferredHeight: 100f);
                return;
            }

            foreach (string category in categories) CreateRow(category);
        }

        private void CreateRow(string category)
        {
            int steps = Mathf.Clamp(Mathf.RoundToInt(PerformanceSettings.GetDensity(category) * StepsPerUnit), 0, MaxSteps);

            CreateSliderRow(category, MaxSteps, steps, FormatPercent,
                newSteps => PerformanceSettings.SetDensity(category, newSteps / (float)StepsPerUnit));
        }

        // FPS alvo em passos de 5 (30, 35 ... 120). Se o aparelho tiver tela de 60 Hz, valores acima de 60 não aparecem na tela.
        private void CreateTargetFpsRow()
        {
            int current = PerformanceSettings.GetTargetFps(PerformanceSettings.DefaultTargetFps);
            int steps = Mathf.RoundToInt((current - PerformanceSettings.MinTargetFps) / (float)PerformanceSettings.TargetFpsStep);

            CreateSliderRow("FPS alvo", MaxFpsSteps, Mathf.Clamp(steps, 0, MaxFpsSteps), FormatFps,
                newSteps => PerformanceSettings.SetTargetFps(FpsFromSteps(newSteps)));
        }

        private void CreateFpsCounterRow()
        {
            RectTransform row = CreateRowContainer("Row Contador de FPS");

            Text label = UiKit.CreateText(row, "Label", "Contador de FPS", 42, TextAnchor.MiddleLeft, UiKit.TextColor);
            UiKit.SetLayout(label.gameObject, preferredWidth: RowLabelWidth, preferredHeight: 100f);

            bool show = PerformanceSettings.GetShowFpsCounter(PerformanceSettings.DefaultShowFpsCounter);
            Button toggle = UiKit.CreateButton(row, "Toggle", string.Empty, new Vector2(0f, 90f), 40, null);
            UiKit.SetLayout(toggle.gameObject, preferredHeight: 90f, flexibleWidth: 1f);
            ApplyToggleLook(toggle, show);

            toggle.onClick.AddListener(() =>
            {
                show = !show;
                PerformanceSettings.SetShowFpsCounter(show);
                ApplyToggleLook(toggle, show);
            });
        }

        private static void ApplyToggleLook(Button toggle, bool on)
        {
            toggle.GetComponentInChildren<Text>().text = on ? "LIGADO" : "DESLIGADO";

            ColorBlock colors = toggle.colors;
            colors.normalColor = on ? Darken(UiKit.AccentColor, 0.6f) : UiKit.ButtonColor;
            toggle.colors = colors;
        }

        /// <summary>Linha "rótulo — slider — valor". <paramref name="onChanged"/> recebe o passo escolhido (0 a <paramref name="maxSteps"/>).</summary>
        private void CreateSliderRow(string labelText, int maxSteps, int currentSteps, System.Func<int, string> format, System.Action<int> onChanged)
        {
            RectTransform row = CreateRowContainer($"Row {labelText}");

            Text label = UiKit.CreateText(row, "Label", labelText, 42, TextAnchor.MiddleLeft, UiKit.TextColor);
            UiKit.SetLayout(label.gameObject, preferredWidth: RowLabelWidth, preferredHeight: 100f);

            Text value = UiKit.CreateText(row, "Value", format(currentSteps), 40, TextAnchor.MiddleRight, UiKit.AccentColor);
            UiKit.SetLayout(value.gameObject, preferredWidth: 170f, preferredHeight: 100f);

            UiKit.CreateSlider(row, "Slider", 0, maxSteps, currentSteps, newValue =>
            {
                int steps = Mathf.RoundToInt(newValue);
                onChanged(steps);
                value.text = format(steps);
            });

            // Ordem visual: rótulo, slider, valor. O valor foi criado antes só para o slider poder atualizá-lo.
            value.transform.SetAsLastSibling();
        }

        private RectTransform CreateRowContainer(string rowName)
        {
            RectTransform row = UiKit.CreateRect(performanceContent, rowName);
            UiKit.AddHorizontalLayout(row.gameObject, 30f, TextAnchor.MiddleCenter);
            UiKit.SetLayout(row.gameObject, preferredHeight: 100f);
            return row;
        }

        private void CreateSectionTitle(string title)
        {
            Text text = UiKit.CreateText(performanceContent, $"Section {title}", title.ToUpper(), 32, TextAnchor.LowerLeft, UiKit.MutedTextColor);
            UiKit.SetLayout(text.gameObject, preferredHeight: 60f);
        }

        private static int FpsFromSteps(int steps) => PerformanceSettings.MinTargetFps + steps * PerformanceSettings.TargetFpsStep;

        private static string FormatPercent(int steps) => $"{steps * PercentPerStep}%";

        private static string FormatFps(int steps) => $"{FpsFromSteps(steps)} FPS";

        // Color * float também multiplicaria o alfa e deixaria a cor translúcida.
        private static Color Darken(Color color, float factor) => new Color(color.r * factor, color.g * factor, color.b * factor, 1f);
    }
}
