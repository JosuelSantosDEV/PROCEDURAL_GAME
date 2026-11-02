#if UNITY_EDITOR || DEV_TOOLS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PROCEDURAL_GAME.UI;
using PROCEDURAL_GAME.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PROCEDURAL_GAME.DevTools
{
    /// <summary>
    /// FERRAMENTA DE DESENVOLVIMENTO (não vai para o jogo final). Botão "DEV" que abre a aba de calibração das árvores do
    /// bioma atual: mostrar/esconder cada tipo e pacote, ajustar a altura (m) e o multiplicador do colisor de cada tipo.
    /// Cria a si mesma ao iniciar o jogo (sem depender de nenhuma cena ou script do jogo), então apagar a pasta Scripts/Dev
    /// remove a ferramenta inteira. Em jogo, abre pausando o tempo; o mundo é refeito a cada mudança.
    /// </summary>
    public sealed class DEV_TuningPanel : MonoBehaviour
    {
        private static readonly Color DEV_OnColor = new Color(0.22f, 0.40f, 0.28f, 1f);

        private RectTransform canvas;
        private GameObject panel;
        private RectTransform content;
        private Text titleText;
        private Text statusText;

        private BiomeProfile biome;
        private WorldGenerator world;
        private float timeScaleBefore = 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void DEV_Bootstrap()
        {
            if (FindObjectOfType<DEV_TuningPanel>() != null) return;

            GameObject host = new GameObject("DEV_TuningPanel");
            DontDestroyOnLoad(host);
            host.AddComponent<DEV_TuningPanel>();
        }

        private void Awake()
        {
            canvas = UiKit.CreateCanvas(transform, "DEV_Canvas", 200);

            // Canto superior esquerdo, abaixo do contador de FPS.
            Button open = UiKit.CreateButton(canvas, "DEV_OpenButton", "DEV", new Vector2(170f, 80f), 36, DEV_Open);
            UiKit.Anchor((RectTransform)open.transform, new Vector2(0f, 1f), new Vector2(20f, -200f), new Vector2(170f, 80f));

            DEV_BuildPanel();
            SceneManager.sceneLoaded += DEV_OnSceneLoaded;
        }

        private void OnDestroy() => SceneManager.sceneLoaded -= DEV_OnSceneLoaded;

        // Trocar de cena fecha a aba; quem carrega a cena já cuida do tempo (o menu volta a 1).
        private void DEV_OnSceneLoaded(Scene scene, LoadSceneMode mode) => panel.SetActive(false);

        // ---- Abrir / fechar ----

        private void DEV_Open()
        {
            world = FindObjectOfType<WorldGenerator>();
            MainMenuController menu = FindObjectOfType<MainMenuController>();
            biome = world != null ? world.Biome : menu != null ? menu.CurrentBiome : null;

            // Em jogo, para o tempo como a pausa faz: dá para olhar as árvores enquanto ajusta.
            if (world != null)
            {
                timeScaleBefore = Time.timeScale;
                Time.timeScale = 0f;
            }

            panel.SetActive(true);
            DEV_Rebuild();
        }

        private void DEV_Close()
        {
            DEV_TuningStore.Instance.DEV_Save();
            panel.SetActive(false);
            if (world != null) Time.timeScale = timeScaleBefore;
        }

        // ---- Montagem ----

        private void DEV_BuildPanel()
        {
            // Coluna à direita: o mundo continua visível à esquerda.
            Image box = UiKit.CreateImage(canvas, "DEV_Panel", new Color(0.03f, 0.04f, 0.04f, 0.92f));
            panel = box.gameObject;
            UiKit.Anchor(box.rectTransform, new Vector2(1f, 0.5f), Vector2.zero, new Vector2(1020f, 1060f));
            UiKit.AddVerticalLayout(panel, 16f, 30, TextAnchor.UpperCenter);

            titleText = UiKit.CreateText(box.rectTransform, "Title", "Desenvolvimento", 44, TextAnchor.MiddleCenter, UiKit.TextColor);
            titleText.fontStyle = FontStyle.Bold;
            UiKit.SetLayout(titleText.gameObject, preferredHeight: 64f);

            RectTransform scroll = UiKit.CreateScrollView(box.rectTransform, "Options", out content);
            UiKit.SetLayout(scroll.gameObject, flexibleHeight: 1f);

            statusText = UiKit.CreateText(box.rectTransform, "Status", string.Empty, 26, TextAnchor.MiddleCenter, UiKit.MutedTextColor);
            UiKit.SetLayout(statusText.gameObject, preferredHeight: 36f);

            RectTransform rowA = UiKit.CreateRect(box.rectTransform, "FooterA");
            UiKit.AddHorizontalLayout(rowA.gameObject, 14f, TextAnchor.MiddleCenter);
            UiKit.SetLayout(rowA.gameObject, preferredHeight: 84f);
            UiKit.CreateButton(rowA, "All", "TODOS", new Vector2(300f, 80f), 32, () => DEV_SetAllEnabled(true));
            UiKit.CreateButton(rowA, "None", "NENHUM", new Vector2(300f, 80f), 32, () => DEV_SetAllEnabled(false));
            UiKit.CreateButton(rowA, "Reset", "RESTAURAR", new Vector2(300f, 80f), 32, DEV_ResetAll);

            RectTransform rowB = UiKit.CreateRect(box.rectTransform, "FooterB");
            UiKit.AddHorizontalLayout(rowB.gameObject, 14f, TextAnchor.MiddleCenter);
            UiKit.SetLayout(rowB.gameObject, preferredHeight: 84f);
            UiKit.CreateButton(rowB, "Copy", "COPIAR VALORES", new Vector2(300f, 80f), 30, DEV_CopyValues);
#if UNITY_EDITOR
            UiKit.CreateButton(rowB, "Write", "GRAVAR NO PERFIL", new Vector2(300f, 80f), 28, DEV_WriteToProfiles);
#endif
            UiKit.CreateButton(rowB, "Close", "FECHAR", new Vector2(300f, 80f), 32, DEV_Close);

            panel.SetActive(false);
        }

        private void DEV_Rebuild()
        {
            foreach (Transform child in content)
            {
                // Desativar antes: o Destroy só age no fim do frame e a linha antiga ainda entraria no layout.
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }

            titleText.text = biome != null ? $"Desenvolvimento — {biome.DisplayName}" : "Desenvolvimento";
            statusText.text = string.Empty;

            List<ScatterProfile> profiles = DEV_TunableProfiles();
            if (profiles.Count == 0)
            {
                Text empty = UiKit.CreateText(content, "Empty", "Este bioma não tem tipos ajustáveis.", 34, TextAnchor.MiddleCenter, UiKit.MutedTextColor);
                UiKit.SetLayout(empty.gameObject, preferredHeight: 120f);
                return;
            }

            foreach (ScatterProfile profile in profiles) DEV_BuildProfile(profile);
        }

        private void DEV_BuildProfile(ScatterProfile profile)
        {
            DEV_TuningStore store = DEV_TuningStore.Instance;

            string name = string.IsNullOrEmpty(profile.category) ? profile.name : profile.category;
            Text section = UiKit.CreateText(content, "Section", $"{name.ToUpper()}  ({profile.name})", 30, TextAnchor.LowerLeft, UiKit.MutedTextColor);
            UiKit.SetLayout(section.gameObject, preferredHeight: 56f);

            // Pacotes (estações): cada um liga/desliga todas as suas variações.
            RectTransform packRow = UiKit.CreateRect(content, "Packs");
            UiKit.AddHorizontalLayout(packRow.gameObject, 12f, TextAnchor.MiddleCenter);
            UiKit.SetLayout(packRow.gameObject, preferredHeight: 80f);

            foreach (string pack in DEV_Packs(profile))
            {
                string packName = pack;
                DEV_CreateToggle(packRow, packName, store.DEV_IsPackEnabled(profile, packName), 200f, 80f, 30, on =>
                {
                    store.DEV_SetPackEnabled(profile, packName, on);
                    DEV_Apply();
                });
            }

            for (int g = 0; g < profile.groups.Length; g++)
            {
                DEV_TuningStore.GroupState state = store.DEV_GetGroup(profile, g);
                if (state == null) continue;

                int group = g;
                ScatterProfile.PrefabGroup data = profile.groups[g];
                string label = string.IsNullOrEmpty(data.displayName) ? data.id : data.displayName;

                DEV_CreateToggle(content, $"{label}  ({data.variants?.Length ?? 0})", state.enabled, 0f, 80f, 34, on =>
                {
                    store.DEV_SetEnabled(profile, group, on);
                    DEV_Apply();
                });

                DEV_CreateStepper("Altura (m)", state.elevation, "0.00", new[] { -1f, -0.1f }, new[] { 0.1f, 1f },
                    value => store.DEV_SetElevation(profile, group, value),
                    () => store.DEV_GetGroup(profile, group).elevation);

                DEV_CreateStepper("Colisor ×", state.colliderScale, "0.00", new[] { -0.1f, -0.01f }, new[] { 0.01f, 0.1f },
                    value => store.DEV_SetColliderScale(profile, group, value),
                    () => store.DEV_GetGroup(profile, group).colliderScale);
            }
        }

        /// <summary>Botão que liga/desliga. Largura 0 = ocupa o que sobrar da linha.</summary>
        private void DEV_CreateToggle(Transform parent, string label, bool initial, float width, float height, int fontSize, Action<bool> onChanged)
        {
            bool state = initial;
            Button button = UiKit.CreateButton(parent, "Toggle", label, new Vector2(width, height), fontSize, null);
            UiKit.SetLayout(button.gameObject, preferredWidth: width > 0f ? width : -1f, preferredHeight: height, flexibleWidth: width > 0f ? -1f : 1f);
            DEV_ApplyToggleLook(button, state);

            button.onClick.AddListener(() =>
            {
                state = !state;
                DEV_ApplyToggleLook(button, state);
                onChanged(state);
            });
        }

        private static void DEV_ApplyToggleLook(Button button, bool on)
        {
            ColorBlock colors = button.colors;
            colors.normalColor = on ? DEV_OnColor : UiKit.ButtonColor;
            colors.selectedColor = colors.normalColor;
            button.colors = colors;

            // Texto cinza = tipo escondido.
            button.GetComponentInChildren<Text>().color = on ? UiKit.TextColor : UiKit.MutedTextColor;
        }

        /// <summary>Linha "rótulo  [-a][-b]  valor  [+c][+d]". Cada botão soma o passo e refaz o mundo.</summary>
        private void DEV_CreateStepper(string label, float current, string format, float[] downSteps, float[] upSteps, Action<float> set, Func<float> get)
        {
            RectTransform row = UiKit.CreateRect(content, "Stepper");
            UiKit.AddHorizontalLayout(row.gameObject, 10f, TextAnchor.MiddleCenter);
            UiKit.SetLayout(row.gameObject, preferredHeight: 76f);

            Text name = UiKit.CreateText(row, "Label", label, 30, TextAnchor.MiddleLeft, UiKit.TextColor);
            UiKit.SetLayout(name.gameObject, preferredWidth: 200f, preferredHeight: 76f);

            Text value = UiKit.CreateText(row, "Value", current.ToString(format, CultureInfo.InvariantCulture), 34, TextAnchor.MiddleCenter, UiKit.AccentColor);
            UiKit.SetLayout(value.gameObject, preferredWidth: 130f, preferredHeight: 76f);

            void Step(float delta)
            {
                set(get() + delta);
                value.text = get().ToString(format, CultureInfo.InvariantCulture);
                DEV_Apply();
            }

            // Ordem visual: menos grande, menos pequeno, valor, mais pequeno, mais grande.
            foreach (float step in downSteps) DEV_CreateStepButton(row, step, Step);
            value.transform.SetAsLastSibling();
            foreach (float step in upSteps) DEV_CreateStepButton(row, step, Step);
        }

        private static void DEV_CreateStepButton(Transform parent, float step, Action<float> onStep)
        {
            string text = (step > 0f ? "+" : "") + step.ToString("0.##", CultureInfo.InvariantCulture);
            Button button = UiKit.CreateButton(parent, "Step", text, new Vector2(120f, 76f), 28, () => onStep(step));
            UiKit.SetLayout(button.gameObject, preferredWidth: 120f, preferredHeight: 76f);
        }

        // ---- Ações ----

        /// <summary>Refaz os objetos dos chunks já carregados com os valores novos (em jogo; no menu não há mundo).</summary>
        private void DEV_Apply()
        {
            if (world != null) world.RepopulateAll();
        }

        private void DEV_SetAllEnabled(bool enabled)
        {
            DEV_TuningStore store = DEV_TuningStore.Instance;

            foreach (ScatterProfile profile in DEV_TunableProfiles())
            {
                for (int g = 0; g < profile.groups.Length; g++) store.DEV_SetEnabled(profile, g, enabled);
                foreach (string pack in DEV_Packs(profile)) store.DEV_SetPackEnabled(profile, pack, true);
            }

            DEV_Apply();
            DEV_Rebuild();
        }

        private void DEV_ResetAll()
        {
            foreach (ScatterProfile profile in DEV_TunableProfiles()) DEV_TuningStore.Instance.DEV_Reset(profile);

            DEV_Apply();
            DEV_Rebuild();
            statusText.text = "Valores restaurados para os do projeto.";
        }

        private void DEV_CopyValues()
        {
            StringBuilder text = new StringBuilder();
            foreach (ScatterProfile profile in DEV_TunableProfiles()) text.Append(DEV_TuningStore.Instance.DEV_BuildReport(profile));

            GUIUtility.systemCopyBuffer = text.ToString();
            Debug.Log($"[DEV] Valores de calibração:\n{text}");
            statusText.text = "Valores copiados (e no log).";
        }

#if UNITY_EDITOR
        private void DEV_WriteToProfiles()
        {
            foreach (ScatterProfile profile in DEV_TunableProfiles()) DEV_TuningStore.Instance.DEV_WriteToProfile(profile);

            DEV_Rebuild();
            statusText.text = "Gravado nos perfis (novo padrão do projeto).";
        }
#endif

        // ---- Dados ----

        private List<ScatterProfile> DEV_TunableProfiles()
        {
            List<ScatterProfile> result = new List<ScatterProfile>();

            if (biome != null && biome.scatter != null)
            {
                foreach (ScatterProfile profile in biome.scatter)
                {
                    if (profile != null && profile.HasGroups) result.Add(profile);
                }
            }

            return result;
        }

        private static List<string> DEV_Packs(ScatterProfile profile)
        {
            List<string> packs = new List<string>();

            foreach (ScatterProfile.PrefabGroup group in profile.groups)
            {
                if (group?.variants == null) continue;

                foreach (ScatterProfile.GroupVariant variant in group.variants)
                {
                    if (!string.IsNullOrEmpty(variant.pack) && !packs.Contains(variant.pack)) packs.Add(variant.pack);
                }
            }

            return packs;
        }
    }
}
#endif
