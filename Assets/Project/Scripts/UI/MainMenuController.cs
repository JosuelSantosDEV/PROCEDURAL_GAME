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

        // Passos do slider de FPS desejado (qualidade automática): 15, 20 ... 60.
        private const int MaxAutoFpsSteps = (PerformanceSettings.MaxAutoFps - PerformanceSettings.MinAutoFps) / PerformanceSettings.AutoFpsStep;

        private const float RowLabelWidth = 380f;

        [Tooltip("Biomas oferecidos, na ordem em que aparecem.")]
        [SerializeField] private BiomeProfile[] biomes;

        [Tooltip("Nome da cena do jogo (precisa estar em File > Build Settings).")]
        [SerializeField] private string gameSceneName = "GameWorld";

        [Tooltip("Câmera do menu: a cor do fundo acompanha a neblina do bioma escolhido.")]
        [SerializeField] private Camera menuCamera;

        // Id (ASCII, chave de gravação) e nome mostrado de cada categoria de objeto do bioma escolhido.
        private readonly struct CategoryOption
        {
            public readonly string Id;
            public readonly string Name;

            public CategoryOption(string id, string name)
            {
                Id = id;
                Name = name;
            }
        }

        private readonly List<CategoryOption> categories = new List<CategoryOption>();

        private int biomeIndex;
        private GameObject mainPanel;
        private GameObject performancePanel;
        private Text biomeNameText;
        private Text biomeDescriptionText;
        private Text performanceTitleText;
        private RectTransform performanceContent;

        /// <summary>Bioma selecionado no menu (lido também pela ferramenta de calibração).</summary>
        public BiomeProfile CurrentBiome => biomes[biomeIndex];

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
            foreach (CategoryOption category in categories) PerformanceSettings.SetDensity(category.Id, PerformanceSettings.DefaultDensity);
            PerformanceSettings.ResetGeneral();
            ControlSettings.Reset();
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
            CreateAutoQualityRow();

            // Com a qualidade automática, o FPS desejado substitui o teto manual (o jogo roda limitado a ele).
            if (PerformanceSettings.GetAutoQuality()) CreateAutoFpsRow();
            else CreateTargetFpsRow();

            CreateFpsCounterRow();
            CreateRenderQualityRow();
            CreateViewDistanceRow();
            CreateShadowRow();
            CreateGroundQualityRow();

            CreateSectionTitle("Controles");
            CreateSliderRow("Sensibilidade", ControlSettings.MaxLookSteps, ControlSettings.GetLookSteps(), ControlSettings.FormatLookSteps, ControlSettings.SetLookSteps);
            CreateSliderRow("Transparência", ControlSettings.MaxTransparencySteps, ControlSettings.GetTransparencySteps(), ControlSettings.FormatTransparencySteps, ControlSettings.SetTransparencySteps);

            CreateSectionTitle(PerformanceSettings.GetAutoQuality() ? "Objetos (máximo)" : "Objetos");

            categories.Clear();
            if (CurrentBiome.scatter != null)
            {
                foreach (ScatterProfile profile in CurrentBiome.scatter)
                {
                    if (profile == null || string.IsNullOrEmpty(profile.categoryId) || ContainsCategory(profile.categoryId)) continue;

                    string name = string.IsNullOrEmpty(profile.category) ? profile.categoryId : profile.category;
                    categories.Add(new CategoryOption(profile.categoryId, name));
                }
            }

            if (categories.Count == 0)
            {
                Text empty = UiKit.CreateText(performanceContent, "Empty", "Este bioma não tem objetos configuráveis.", 36, TextAnchor.MiddleCenter, UiKit.MutedTextColor);
                UiKit.SetLayout(empty.gameObject, preferredHeight: 100f);
                return;
            }

            foreach (CategoryOption category in categories) CreateRow(category);
        }

        private bool ContainsCategory(string id)
        {
            foreach (CategoryOption category in categories)
            {
                if (category.Id == id) return true;
            }

            return false;
        }

        private void CreateRow(CategoryOption category)
        {
            int steps = Mathf.Clamp(Mathf.RoundToInt(PerformanceSettings.GetDensity(category.Id) * StepsPerUnit), 0, MaxSteps);

            CreateSliderRow(category.Name, MaxSteps, steps, FormatPercent,
                newSteps => PerformanceSettings.SetDensity(category.Id, newSteps / (float)StepsPerUnit));
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
            CreateToggleRow("Contador de FPS", PerformanceSettings.GetShowFpsCounter(PerformanceSettings.DefaultShowFpsCounter),
                PerformanceSettings.SetShowFpsCounter);
        }

        // Resolução de renderização: Baixo 50%, Performance 60%, Médio 75% (padrão), Alto 85%, Ultra 100% (nativa, com pós-processamento).
        // Com a qualidade automática, é o teto (ela desce de degrau em degrau quando o FPS cai). O HDR fica sempre desligado.
        private void CreateRenderQualityRow()
        {
            int level = PerformanceSettings.GetRenderQuality();
            string label = PerformanceSettings.GetAutoQuality() ? "Resolução (máx.)" : "Resolução";

            Text hint = null;
            CreateSliderRow(label, PerformanceSettings.RenderUltra, level, FormatRenderQuality, newLevel =>
            {
                PerformanceSettings.SetRenderQuality(newLevel);
                if (hint != null) hint.text = RenderQualityHint(newLevel);
            });

            hint = UiKit.CreateText(performanceContent, "RenderQualityHint", RenderQualityHint(level), 26, TextAnchor.UpperLeft, UiKit.MutedTextColor);
            UiKit.SetLayout(hint.gameObject, preferredHeight: 100f);
        }

        private static string FormatRenderQuality(int level)
        {
            switch (level)
            {
                case PerformanceSettings.RenderLow: return "Baixo";
                case PerformanceSettings.RenderPerformance: return "Performance";
                case PerformanceSettings.RenderHigh: return "Alto";
                case PerformanceSettings.RenderUltra: return "Ultra";
                default: return "Médio";
            }
        }

        private static string RenderQualityHint(int level)
        {
            int percent = Mathf.RoundToInt(PerformanceSettings.GetRenderScale(level) * 100f);

            switch (level)
            {
                case PerformanceSettings.RenderLow: return $"Baixo ({percent}% da resolução): imagem mais suave e o jogo bem mais leve. A melhor escolha para celulares fracos.";
                case PerformanceSettings.RenderPerformance: return $"Performance ({percent}% da resolução): leve, um pouco mais nítido que o Baixo.";
                case PerformanceSettings.RenderHigh: return $"Alto ({percent}% da resolução): imagem nítida, mais pesado que o Médio.";
                case PerformanceSettings.RenderUltra: return $"Ultra ({percent}%, resolução nativa da tela) com pós-processamento: mais contraste, brilho nas luzes, vinheta e tom cinematográfico. Só para celulares fortes.";
                default: return $"Médio ({percent}% da resolução): equilíbrio entre nitidez e desempenho. É o padrão.";
            }
        }

        // Qualidade do solo: liga ou desliga o relevo (mapa de normal) das texturas de chão. Padrão desligada.
        private void CreateGroundQualityRow()
        {
            CreateToggleRow("Qualidade do solo", PerformanceSettings.GetGroundQuality(), PerformanceSettings.SetGroundQuality);

            Text hint = UiKit.CreateText(performanceContent, "GroundQualityHint",
                "Ligada: o chão ganha relevo (folhas e terra em alto e baixo), mais bonito e mais pesado. Desligada: textura lisa, mais leve. Só tem efeito nos biomas com mapa de normal no chão.",
                26, TextAnchor.UpperLeft, UiKit.MutedTextColor);
            UiKit.SetLayout(hint.gameObject, preferredHeight: 100f);
        }

        // Distância de visão: Perto, Médio, Longe. Define quanto do mundo existe, onde a neblina cobre tudo e até onde a câmera
        // desenha. Com a qualidade automática, é o teto (ela pode reduzir em jogo).
        private void CreateViewDistanceRow()
        {
            int level = PerformanceSettings.GetViewLevel();
            string label = PerformanceSettings.GetAutoQuality() ? "Raio (máx.)" : "Raio de visão";

            Text hint = null;
            CreateSliderRow(label, PerformanceSettings.ViewFar, level, FormatViewDistance, newLevel =>
            {
                PerformanceSettings.SetViewLevel(newLevel);
                if (hint != null) hint.text = ViewDistanceHint(newLevel);
            });

            hint = UiKit.CreateText(performanceContent, "ViewDistanceHint", ViewDistanceHint(level), 26, TextAnchor.UpperLeft, UiKit.MutedTextColor);
            UiKit.SetLayout(hint.gameObject, preferredHeight: 80f);
        }

        private static string FormatViewDistance(int level)
        {
            switch (level)
            {
                case PerformanceSettings.ViewNear: return "Perto";
                case PerformanceSettings.ViewFar: return "Longe";
                default: return "Médio";
            }
        }

        private static string ViewDistanceHint(int level)
        {
            string meters = $"{PerformanceSettings.GetViewDistance(level):0} m";

            switch (level)
            {
                case PerformanceSettings.ViewNear: return $"Perto ({meters}): neblina mais fechada e bem menos objetos na tela. A opção mais leve.";
                case PerformanceSettings.ViewFar: return $"Longe ({meters}): enxerga mais longe, com neblina mais leve. Pesa mais no celular.";
                default: return $"Médio ({meters}): equilíbrio entre alcance e desempenho. É o padrão.";
            }
        }

        // Sombras: 0 a 3. Com a qualidade automática, é o teto (ela reduz as sombras primeiro).
        private void CreateShadowRow()
        {
            int level = PerformanceSettings.GetShadowLevel();
            string label = PerformanceSettings.GetAutoQuality() ? "Sombras (máx.)" : "Sombras";

            Text hint = null;
            CreateSliderRow(label, PerformanceSettings.ShadowHigh, level, FormatShadows, newLevel =>
            {
                PerformanceSettings.SetShadowLevel(newLevel);
                if (hint != null) hint.text = ShadowHint(newLevel);
            });

            hint = UiKit.CreateText(performanceContent, "ShadowHint", ShadowHint(level), 26, TextAnchor.UpperLeft, UiKit.MutedTextColor);
            UiKit.SetLayout(hint.gameObject, preferredHeight: 80f);
        }

        private static string ShadowHint(int level)
        {
            switch (level)
            {
                case PerformanceSettings.ShadowLow: return "Baixa: só as árvores projetam sombra, perto de você. A mais barata.";
                case PerformanceSettings.ShadowMedium: return "Média: árvores e rochas, com sombra suave e um pouco mais longe.";
                case PerformanceSettings.ShadowHigh: return "Alta: o maior alcance de sombras. Pesa mais no celular.";
                default: return "Sem sombras: o jogo fica mais leve e mais plano.";
            }
        }

        private static string FormatShadows(int level)
        {
            switch (level)
            {
                case PerformanceSettings.ShadowLow: return "Baixa";
                case PerformanceSettings.ShadowMedium: return "Média";
                case PerformanceSettings.ShadowHigh: return "Alta";
                default: return "Desligadas";
            }
        }

        // Liga a qualidade automática e troca as linhas de FPS (por isso refaz a lista).
        private void CreateAutoQualityRow()
        {
            bool enabled = PerformanceSettings.GetAutoQuality();

            CreateToggleRow("Qualidade automática", enabled, on =>
            {
                PerformanceSettings.SetAutoQuality(on);
                RebuildRows();
            });

            if (!enabled) return;

            Text hint = UiKit.CreateText(performanceContent, "AutoHint",
                "O jogo mede o FPS e reduz o raio de visão e a quantidade de objetos para alcançar o FPS desejado, e devolve a qualidade quando sobra folga. Os valores de Objetos abaixo viram o máximo.",
                28, TextAnchor.UpperLeft, UiKit.MutedTextColor);
            UiKit.SetLayout(hint.gameObject, preferredHeight: 120f);
        }

        // FPS desejado da qualidade automática: de 15 a 60 em passos de 5.
        private void CreateAutoFpsRow()
        {
            int current = PerformanceSettings.GetAutoTargetFps();
            int steps = Mathf.RoundToInt((current - PerformanceSettings.MinAutoFps) / (float)PerformanceSettings.AutoFpsStep);

            CreateSliderRow("FPS desejado", MaxAutoFpsSteps, Mathf.Clamp(steps, 0, MaxAutoFpsSteps), FormatAutoFps,
                newSteps => PerformanceSettings.SetAutoTargetFps(AutoFpsFromSteps(newSteps)));
        }

        private void CreateToggleRow(string labelText, bool current, System.Action<bool> onChanged)
        {
            RectTransform row = CreateRowContainer("ToggleRow");

            Text label = UiKit.CreateText(row, "Label", labelText, 42, TextAnchor.MiddleLeft, UiKit.TextColor);
            UiKit.SetLayout(label.gameObject, preferredWidth: RowLabelWidth, preferredHeight: 100f);

            bool state = current;
            Button toggle = UiKit.CreateButton(row, "Toggle", string.Empty, new Vector2(0f, 90f), 40, null);
            UiKit.SetLayout(toggle.gameObject, preferredHeight: 90f, flexibleWidth: 1f);
            ApplyToggleLook(toggle, state);

            toggle.onClick.AddListener(() =>
            {
                state = !state;

                // Atualizar o botão ANTES: onChanged pode refazer a lista (desativando esta linha), e o texto do botão
                // não é mais encontrado em objeto inativo.
                ApplyToggleLook(toggle, state);
                onChanged(state);
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
            UiKit.CreateSliderRow(performanceContent, labelText, RowLabelWidth, maxSteps, currentSteps, format, onChanged);
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
            Text text = UiKit.CreateText(performanceContent, "SectionTitle", title.ToUpper(), 32, TextAnchor.LowerLeft, UiKit.MutedTextColor);
            UiKit.SetLayout(text.gameObject, preferredHeight: 60f);
        }

        private static int FpsFromSteps(int steps) => PerformanceSettings.MinTargetFps + steps * PerformanceSettings.TargetFpsStep;

        private static string FormatPercent(int steps) => $"{steps * PercentPerStep}%";

        private static string FormatFps(int steps) => $"{FpsFromSteps(steps)} FPS";

        private static int AutoFpsFromSteps(int steps) => PerformanceSettings.MinAutoFps + steps * PerformanceSettings.AutoFpsStep;

        private static string FormatAutoFps(int steps) => $"{AutoFpsFromSteps(steps)} FPS";

        // Color * float também multiplicaria o alfa e deixaria a cor translúcida.
        private static Color Darken(Color color, float factor) => new Color(color.r * factor, color.g * factor, color.b * factor, 1f);
    }
}
