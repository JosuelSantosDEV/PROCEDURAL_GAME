#if UNITY_EDITOR || DEV_TOOLS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PROCEDURAL_GAME.CameraControl;
using PROCEDURAL_GAME.Player;
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
        private static readonly int DEV_TileVariationOffId = Shader.PropertyToID("_TileVariationOff");

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

            // Atalho para achar água (os lagos são raros de propósito): só em jogo e em biomas com lagos.
            if (world != null && world.Lakes != null)
            {
                Button lake = UiKit.CreateButton(content, "GoToLake", "IR AO LAGO MAIS PRÓXIMO", new Vector2(0f, 90f), 34, DEV_GoToNearestLake);
                UiKit.SetLayout(lake.gameObject, preferredHeight: 90f, flexibleWidth: 1f);
            }

            DEV_BuildCamera();
            DEV_BuildHeadLamp();
            DEV_BuildSurvival();

            // Para medir no aparelho quanto a variação da textura do chão custa. É global: vale até fechar o jogo.
            DEV_CreateToggle(content, "VARIAÇÃO DA TEXTURA DO SOLO", Shader.GetGlobalFloat(DEV_TileVariationOffId) < 0.5f, 0f, 80f, 34,
                on => Shader.SetGlobalFloat(DEV_TileVariationOffId, on ? 0f : 1f));

            List<ScatterProfile> profiles = DEV_TunableProfiles();
            if (profiles.Count == 0)
            {
                Text empty = UiKit.CreateText(content, "Empty", "Este bioma não tem tipos ajustáveis.", 34, TextAnchor.MiddleCenter, UiKit.MutedTextColor);
                UiKit.SetLayout(empty.gameObject, preferredHeight: 120f);
                return;
            }

            foreach (ScatterProfile profile in profiles) DEV_BuildProfile(profile);
        }

        // Câmera da 3ª pessoa: para achar a distância e a altura ideais no aparelho. Vale só nesta partida; COPIAR VALORES leva os números.
        // Com o painel aberto o tempo está parado, mas a câmera já muda de lugar na hora.
        private void DEV_BuildCamera()
        {
            PlayerCameraController cam = FindObjectOfType<PlayerCameraController>();
            if (cam == null) return;

            Text section = UiKit.CreateText(content, "Section", "CÂMERA (3ª PESSOA)", 30, TextAnchor.LowerLeft, UiKit.MutedTextColor);
            UiKit.SetLayout(section.gameObject, preferredHeight: 56f);

            DEV_CreateStepper("Distância (m)", cam.ThirdPersonDistance, "0.0", new[] { -1f, -0.5f }, new[] { 0.5f, 1f },
                v => cam.ThirdPersonDistance = v, () => cam.ThirdPersonDistance, false);
            DEV_CreateStepper("Altura (m)", cam.PivotHeight, "0.0", new[] { -0.5f, -0.1f }, new[] { 0.1f, 0.5f },
                v => cam.PivotHeight = v, () => cam.PivotHeight, false);
        }

        // Capacete com lanterna: encaixe na cabeça e luz, ao vivo. Vale só nesta partida; COPIAR VALORES leva os números.
        private void DEV_BuildHeadLamp()
        {
            HeadLamp lamp = FindObjectOfType<HeadLamp>();
            if (lamp == null || !lamp.enabled) return;

            Text section = UiKit.CreateText(content, "Section", "CAPACETE E LANTERNA", 30, TextAnchor.LowerLeft, UiKit.MutedTextColor);
            UiKit.SetLayout(section.gameObject, preferredHeight: 56f);

            DEV_CreateToggle(content, "LANTERNA LIGADA", lamp.IsOn, 0f, 80f, 34, on => lamp.SetOn(on));
            DEV_CreateToggle(content, "NOITE (TESTE)", DEV_nightOn, 0f, 80f, 34, DEV_SetNight);
            DEV_CreateToggle(content, "CAPACETE GIRADO 180°", lamp.HelmetBackwards, 0f, 80f, 34, on => lamp.HelmetBackwards = on);
            DEV_CreateToggle(content, "ESCONDER CABELO", lamp.HideHair, 0f, 80f, 34, on => lamp.HideHair = on);

            DEV_CreateStepper("Altura (m)", lamp.GearOffset.y, "0.00", new[] { -0.05f, -0.01f }, new[] { 0.01f, 0.05f },
                v => lamp.GearOffset = new Vector3(lamp.GearOffset.x, v, lamp.GearOffset.z), () => lamp.GearOffset.y, false);
            DEV_CreateStepper("Frente (m)", lamp.GearOffset.z, "0.00", new[] { -0.05f, -0.01f }, new[] { 0.01f, 0.05f },
                v => lamp.GearOffset = new Vector3(lamp.GearOffset.x, lamp.GearOffset.y, v), () => lamp.GearOffset.z, false);
            DEV_CreateStepper("Escala", lamp.GearScale, "0.00", new[] { -0.1f, -0.02f }, new[] { 0.02f, 0.1f },
                v => lamp.GearScale = v, () => lamp.GearScale, false);
            DEV_CreateStepper("Incl. capacete", lamp.GearPitch, "0", new[] { -5f, -1f }, new[] { 1f, 5f },
                v => lamp.GearPitch = v, () => lamp.GearPitch, false);
            DEV_CreateStepper("Incl. lanterna", lamp.LampPitch, "0", new[] { -5f, -1f }, new[] { 1f, 5f },
                v => lamp.LampPitch = v, () => lamp.LampPitch, false);
            DEV_CreateStepper("Luz (int.)", lamp.Intensity, "0.0", new[] { -1f, -0.2f }, new[] { 0.2f, 1f },
                v => lamp.Intensity = v, () => lamp.Intensity, false);
            DEV_CreateStepper("Alcance (m)", lamp.Range, "0", new[] { -10f, -2f }, new[] { 2f, 10f },
                v => lamp.Range = v, () => lamp.Range, false);
            DEV_CreateStepper("Ângulo (°)", lamp.SpotAngle, "0", new[] { -10f, -2f }, new[] { 2f, 10f },
                v => lamp.SpotAngle = v, () => lamp.SpotAngle, false);
        }

        // Noite de teste (até existir o ciclo de dia e noite): escurece sol, ambiente, neblina e céu; desligar devolve os valores.
        private static bool DEV_nightOn;
        private static Color DEV_dayAmbient;
        private static Color DEV_dayFog;
        private static float DEV_daySun;
        private static Color DEV_dayBackground;

        private static void DEV_SetNight(bool on)
        {
            if (on == DEV_nightOn) return;

            Light sun = RenderSettings.sun;
            if (sun == null)
            {
                foreach (Light candidate in FindObjectsOfType<Light>())
                {
                    if (candidate.type != LightType.Directional) continue;
                    sun = candidate;
                    break;
                }
            }

            Camera cam = Camera.main;

            if (on)
            {
                DEV_dayAmbient = RenderSettings.ambientLight;
                DEV_dayFog = RenderSettings.fogColor;
                DEV_daySun = sun != null ? sun.intensity : 0f;
                DEV_dayBackground = cam != null ? cam.backgroundColor : Color.black;

                RenderSettings.ambientLight = DEV_dayAmbient * 0.12f;
                RenderSettings.fogColor = DEV_dayFog * 0.1f;
                if (sun != null) sun.intensity = DEV_daySun * 0.04f;
                if (cam != null) cam.backgroundColor = RenderSettings.fogColor;
            }
            else
            {
                RenderSettings.ambientLight = DEV_dayAmbient;
                RenderSettings.fogColor = DEV_dayFog;
                if (sun != null) sun.intensity = DEV_daySun;
                if (cam != null) cam.backgroundColor = DEV_dayBackground;
            }

            DEV_nightOn = on;
        }

        // Sobrevivência: ainda não há comida no mundo nem ataques; estes botões testam o que depende deles. Só em jogo.
        private void DEV_BuildSurvival()
        {
            PlayerStats stats = FindObjectOfType<PlayerStats>();
            if (stats == null) return;

            DEV_CreateToggle(content, "SEDE E FOME CONTANDO", !PlayerStats.DEV_NeedsPaused, 0f, 80f, 34, on => PlayerStats.DEV_NeedsPaused = !on);

            RectTransform row = UiKit.CreateRect(content, "Survival");
            UiKit.AddHorizontalLayout(row.gameObject, 12f, TextAnchor.MiddleCenter);
            UiKit.SetLayout(row.gameObject, preferredHeight: 80f);

            UiKit.CreateButton(row, "Drink", "BEBER", new Vector2(240f, 80f), 30, stats.Drink);
            UiKit.CreateButton(row, "Eat", "COMER", new Vector2(240f, 80f), 30, stats.Eat);
            UiKit.CreateButton(row, "Hit", "ATAQUE -25%", new Vector2(280f, 80f), 30, () => stats.TakeDamage(0.25f));
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
        // repopulate: refazer os objetos do mundo a cada passo (só para os ajustes de árvores).
        private void DEV_CreateStepper(string label, float current, string format, float[] downSteps, float[] upSteps, Action<float> set, Func<float> get, bool repopulate = true)
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
                if (repopulate) DEV_Apply();
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

        /// <summary>Leva o Player ao lago mais próximo (até ~4 km), 28 m da margem: no platô das palmeiras, olhando a água.</summary>
        private void DEV_GoToNearestLake()
        {
            Vector3 position = world.TargetPosition;
            if (!world.Lakes.TryFindNearest(position.x, position.z, 8, 28f, out Vector2 point, out float distance))
            {
                statusText.text = "Nenhum lago em ~4 km.";
                return;
            }

            DEV_Close();
            world.TeleportTarget(point.x, point.y);
            Debug.Log($"[DEV] Teleportado para um lago a {distance:0} m (ponto {point.x:0}, {point.y:0}).");
        }

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
            PlayerCameraController cam = FindObjectOfType<PlayerCameraController>();
            if (cam != null)
            {
                text.Append(string.Format(CultureInfo.InvariantCulture, "Camera 3a pessoa: distancia {0:0.0} m, altura {1:0.0} m\n",
                    cam.ThirdPersonDistance, cam.PivotHeight));
            }

            HeadLamp lamp = FindObjectOfType<HeadLamp>();
            if (lamp != null)
            {
                text.Append(string.Format(CultureInfo.InvariantCulture,
                    "Capacete: altura {0:0.00} m, frente {1:0.00} m, escala {2:0.00}, inclinacao {8:0}, lanterna {9:0}, girado {3}, sem cabelo {4}; luz {5:0.0}, alcance {6:0} m, angulo {7:0}\n",
                    lamp.GearOffset.y, lamp.GearOffset.z, lamp.GearScale, lamp.HelmetBackwards, lamp.HideHair, lamp.Intensity, lamp.Range, lamp.SpotAngle,
                    lamp.GearPitch, lamp.LampPitch));
            }

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
