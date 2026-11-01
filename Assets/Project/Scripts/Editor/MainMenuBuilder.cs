using System.Collections.Generic;
using System.IO;
using PROCEDURAL_GAME.Core;
using PROCEDURAL_GAME.UI;
using PROCEDURAL_GAME.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

namespace PROCEDURAL_GAME.EditorTools
{
    /// <summary>
    /// Cria a cena do menu inicial (Assets/Scenes/MainMenu.unity) e coloca as duas cenas em Build Settings
    /// (menu primeiro). Antes, abre o GameWorld e roda o Create Procedural World para os biomas, os perfis de
    /// objetos e o botão de pausa existirem. A cena do menu é recriada a cada execução; a UI em si é montada
    /// por código em tempo de execução (<see cref="MainMenuController"/>), então não há layout para editar na cena.
    /// </summary>
    public static class MainMenuBuilder
    {
        private const string MenuScenePath = "Assets/Scenes/MainMenu.unity";
        private const string GameScenePath = "Assets/Scenes/GameWorld.unity";
        private const string BiomeFolder = "Assets/Project/Biomes";

        // Ordem em que os biomas aparecem no menu; biomas novos na pasta entram depois destes.
        private static readonly string[] BiomeOrder = { "Campo", "Floresta_01", "Floresta_02" };

        [MenuItem("PROCEDURAL_GAME/Create Main Menu")]
        public static void Build()
        {
            if (!File.Exists(GameScenePath))
            {
                Debug.LogError($"Cena do jogo não encontrada em {GameScenePath}.");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            // O mundo precisa estar completo (biomas, perfis, botão de pausa) antes de montar o menu.
            EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
            WorldBuilder.Build();
            EditorSceneManager.SaveOpenScenes();

            BiomeProfile[] biomes = LoadBiomes();
            if (biomes.Length == 0)
            {
                Debug.LogError($"Nenhum BiomeProfile em {BiomeFolder}. Rode PROCEDURAL_GAME > Create Procedural World e veja o Console.");
                return;
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Camera camera = CreateCamera();
            CreateEventSystem();
            CreateMenu(biomes, camera);

            EditorSceneManager.SaveScene(scene, MenuScenePath);
            RegisterScenes();

            Debug.Log($"Menu criado em {MenuScenePath} com {biomes.Length} bioma(s) e adicionado ao Build Settings (primeiro). Dê Play para testar. Para voltar ao jogo direto, abra Assets/Scenes/GameWorld.unity.");
        }

        private static Camera CreateCamera()
        {
            GameObject go = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            go.tag = "MainCamera";

            Camera camera = go.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            return camera;
        }

        private static void CreateEventSystem()
        {
            GameObject go = new GameObject("EventSystem", typeof(EventSystem));

            // ObjectFactory executa o Reset do componente, que já atribui o asset de actions padrão do pacote.
            InputSystemUIInputModule module = ObjectFactory.AddComponent<InputSystemUIInputModule>(go);
            if (module.actionsAsset == null)
            {
                Debug.LogWarning("EventSystem do menu: o 'Actions Asset' do Input System UI Input Module está vazio. Selecione o EventSystem e use 'Assign Default Actions'.", module);
            }
        }

        private static void CreateMenu(BiomeProfile[] biomes, Camera camera)
        {
            // FrameRateController: o menu também roda no FPS escolhido (no Android o padrão do Unity é 30).
            GameObject go = new GameObject("MainMenu", typeof(MainMenuController), typeof(FrameRateController));

            SerializedObject serialized = new SerializedObject(go.GetComponent<MainMenuController>());
            SerializedProperty list = serialized.FindProperty("biomes");
            list.arraySize = biomes.Length;
            for (int i = 0; i < biomes.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = biomes[i];

            serialized.FindProperty("menuCamera").objectReferenceValue = camera;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static BiomeProfile[] LoadBiomes()
        {
            List<BiomeProfile> result = new List<BiomeProfile>();

            foreach (string biomeName in BiomeOrder)
            {
                BiomeProfile biome = AssetDatabase.LoadAssetAtPath<BiomeProfile>($"{BiomeFolder}/{biomeName}.asset");
                if (biome != null) result.Add(biome);
            }

            // Biomas criados depois, na mesma pasta, entram no fim (ordem alfabética para ser estável).
            List<BiomeProfile> others = new List<BiomeProfile>();
            foreach (string guid in AssetDatabase.FindAssets("t:BiomeProfile", new[] { BiomeFolder }))
            {
                BiomeProfile biome = AssetDatabase.LoadAssetAtPath<BiomeProfile>(AssetDatabase.GUIDToAssetPath(guid));
                if (biome != null && !result.Contains(biome)) others.Add(biome);
            }

            others.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            result.AddRange(others);
            return result.ToArray();
        }

        private static void RegisterScenes()
        {
            // Menu primeiro (é a cena que abre no celular), jogo em seguida; outras cenas que já estavam ficam depois.
            List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(MenuScenePath, true),
                new EditorBuildSettingsScene(GameScenePath, true)
            };

            foreach (EditorBuildSettingsScene existing in EditorBuildSettings.scenes)
            {
                if (existing.path != MenuScenePath && existing.path != GameScenePath) scenes.Add(existing);
            }

            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
