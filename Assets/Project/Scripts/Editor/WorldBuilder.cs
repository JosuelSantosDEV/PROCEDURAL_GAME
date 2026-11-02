using PROCEDURAL_GAME.Core;
using PROCEDURAL_GAME.DebugTools;
using PROCEDURAL_GAME.UI;
using PROCEDURAL_GAME.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PROCEDURAL_GAME.EditorTools
{
    /// <summary>
    /// Cria ou atualiza o mundo procedural na cena aberta. Pode ser executado quantas vezes quiser:
    /// só preenche o que falta e nunca sobrescreve ajustes que você já fez no Inspector.
    /// - objeto "World" com <see cref="WorldGenerator"/> e <see cref="AtmosphereController"/>;
    /// - material do terreno (shader de cor de vértice);
    /// - perfis de bioma de exemplo (Campo e Floresta) em Assets/Project/Biomes;
    /// - perfis de objetos (árvores, pedras) em Assets/Project/Scatter, ligados aos biomas (<see cref="ScatterBuilder"/>);
    /// - desativa o Terrain antigo, que ficaria sobreposto aos chunks.
    /// </summary>
    public static class WorldBuilder
    {
        private const string UndoName = "Create Procedural World";
        private const string MaterialFolder = "Assets/Project/Materials";
        private const string MaterialPath = MaterialFolder + "/ProceduralTerrain.mat";
        private const string ShaderPath = "Assets/Project/Shaders/TerrainVertexColor.shader";
        private const string BiomeFolder = "Assets/Project/Biomes";

        [MenuItem("PROCEDURAL_GAME/Create Procedural World")]
        public static void Build()
        {
            Material material = GetOrCreateMaterial();
            if (material == null) return;

            BiomeProfile meadow = GetOrCreateBiome("Campo", ConfigureMeadow);

            // A antiga "Floresta" (árvores secas) virou Floresta_02. Renomear mantém o GUID, então a cena continua apontando para ela.
            RenameBiomeIfNeeded("Floresta", "Floresta_02");
            BiomeProfile forest01 = GetOrCreateBiome("Floresta_01", ConfigureForest01);
            MigrateForest01(forest01);
            BiomeProfile forest02 = GetOrCreateBiome("Floresta_02", ConfigureForest02);
            ScatterBuilder.EnsureDefaults(meadow, forest01, forest02);

            SetMenuTexts(meadow, "Campo", "Campo aberto, com pouca neblina. Só rochas.");
            SetMenuTexts(forest01, "Floresta 01", "Mata fechada e viva: árvores variadas e arbustos.");
            SetMenuTexts(forest02, "Floresta 02", "Floresta sombria de árvores secas e rochas.");

            WorldGenerator generator = Object.FindObjectOfType<WorldGenerator>();
            if (generator == null)
            {
                GameObject created = new GameObject("World", typeof(WorldGenerator));
                Undo.RegisterCreatedObjectUndo(created, UndoName);
                generator = created.GetComponent<WorldGenerator>();
            }

            GameObject world = generator.gameObject;
            if (world.GetComponent<AtmosphereController>() == null) Undo.AddComponent<AtmosphereController>(world);
            if (world.GetComponent<AdaptiveQuality>() == null) Undo.AddComponent<AdaptiveQuality>(world);
            if (world.GetComponent<FrameRateController>() == null) Undo.AddComponent<FrameRateController>(world);
            if (world.GetComponent<FpsCounter>() == null) Undo.AddComponent<FpsCounter>(world);
            if (world.GetComponent<InGameMenu>() == null) Undo.AddComponent<InGameMenu>(world);

            // Só preenche campos vazios: se você já trocou o perfil ou o material no Inspector, isso é mantido.
            SerializedObject serialized = new SerializedObject(generator);
            AssignIfEmpty(serialized, "terrainMaterial", material);
            AssignIfEmpty(serialized, "biome", forest01 != null ? forest01 : meadow);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // Grava neblina, ambiente, luz e céu na própria cena. Isso é essencial para o build: com a neblina
            // desligada na cena, o Unity (Graphics > Fog Modes = Automatic) descarta as variantes de shader de
            // neblina, e ela some no celular mesmo funcionando no Editor.
            world.GetComponent<AtmosphereController>().Apply(generator.Biome);

            // O Terrain de teste (50x50) ficaria no meio dos chunks. Só desativamos; apague quando tiver certeza.
            foreach (Terrain terrain in Object.FindObjectsOfType<Terrain>())
            {
                Undo.RecordObject(terrain.gameObject, UndoName);
                terrain.gameObject.SetActive(false);
                Debug.Log($"Terrain '{terrain.name}' desativado (continua na cena, só desligado).");
            }

            EditorSceneManager.MarkSceneDirty(world.scene);
            Selection.activeGameObject = world;
            Debug.Log("Mundo procedural pronto. Salve a cena (Ctrl+S) e dê Play. Para comparar visuais, troque o campo 'Biome' do objeto World entre Campo, Floresta_01 e Floresta_02 e rode este menu de novo para gravar a neblina do bioma na cena.");
        }

        private static void AssignIfEmpty(SerializedObject serialized, string propertyName, Object value)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property.objectReferenceValue == null) property.objectReferenceValue = value;
        }

        private static Material GetOrCreateMaterial()
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null)
            {
                Debug.LogError($"Shader não encontrado em {ShaderPath}. Espere o Unity terminar de importar e tente de novo; se houver erro de shader no Console, me envie.");
                return null;
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                EnsureFolder("Assets/Project", "Materials");
                material = new Material(shader) { name = "ProceduralTerrain" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else if (material.shader != shader)
            {
                // Material criado na versão anterior (URP/Lit): passa a usar o shader de cor de vértice.
                material.shader = shader;
                EditorUtility.SetDirty(material);
            }

            AssetDatabase.SaveAssets();
            return material;
        }

        // Nome e frase do menu inicial. Só preenche se vazio: o que você escreveu no asset é mantido.
        private static void SetMenuTexts(BiomeProfile biome, string displayName, string description)
        {
            if (biome == null || !string.IsNullOrEmpty(biome.displayName)) return;

            biome.displayName = displayName;
            biome.description = description;
            EditorUtility.SetDirty(biome);
            AssetDatabase.SaveAssets();
        }

        private static void RenameBiomeIfNeeded(string oldName, string newName)
        {
            string oldPath = $"{BiomeFolder}/{oldName}.asset";
            if (AssetDatabase.LoadAssetAtPath<BiomeProfile>(oldPath) == null) return;
            if (AssetDatabase.LoadAssetAtPath<BiomeProfile>($"{BiomeFolder}/{newName}.asset") != null) return;

            // RenameAsset devolve o texto do erro (vazio se deu certo).
            string error = AssetDatabase.RenameAsset(oldPath, newName);
            if (!string.IsNullOrEmpty(error)) Debug.LogError($"Não consegui renomear o bioma '{oldName}' para '{newName}': {error}");
        }

        private static BiomeProfile GetOrCreateBiome(string biomeName, System.Action<BiomeProfile> configure)
        {
            string path = $"{BiomeFolder}/{biomeName}.asset";
            BiomeProfile biome = AssetDatabase.LoadAssetAtPath<BiomeProfile>(path);
            if (biome != null) return biome;

            EnsureFolder("Assets/Project", "Biomes");
            biome = ScriptableObject.CreateInstance<BiomeProfile>();
            configure(biome);
            AssetDatabase.CreateAsset(biome, path);
            AssetDatabase.SaveAssets();
            return biome;
        }

        // Campo: aberto, claro, neblina leve. É o padrão do BiomeProfile; só garantimos os valores aqui.
        private static void ConfigureMeadow(BiomeProfile biome)
        {
            biome.lowColor = new Color(0.17f, 0.19f, 0.10f);
            biome.grassColor = new Color(0.27f, 0.35f, 0.17f);
            biome.highColor = new Color(0.45f, 0.45f, 0.36f);
            biome.rockColor = new Color(0.34f, 0.34f, 0.33f);
            biome.fogColor = new Color(0.55f, 0.57f, 0.60f);
            biome.fogDensity = 0.014f;
            biome.ambientColor = new Color(0.30f, 0.32f, 0.35f);
            biome.lightColor = new Color(0.80f, 0.82f, 0.85f);
            biome.lightIntensity = 0.8f;
        }

        // Floresta_01: mata viva e variada; mais verde e fechada que o campo, mas ainda clara perto da Floresta_02.
        private static void ConfigureForest01(BiomeProfile biome)
        {
            biome.lowColor = new Color(0.10f, 0.14f, 0.07f);
            biome.grassColor = new Color(0.18f, 0.27f, 0.12f);
            biome.highColor = new Color(0.30f, 0.32f, 0.22f);
            biome.rockColor = new Color(0.28f, 0.28f, 0.27f);
            biome.fogColor = new Color(0.40f, 0.46f, 0.44f);
            // Mata fechada: enxerga ~70 m (3 chunks de raio), então carrega bem menos árvores que o campo aberto.
            biome.viewRadius = 3;
            biome.fogDensity = 0.024f;
            biome.ambientColor = new Color(0.22f, 0.26f, 0.26f);
            biome.lightColor = new Color(0.70f, 0.76f, 0.80f);
            biome.lightIntensity = 0.65f;
        }

        // A Floresta_01 criada antes tinha neblina 0,020 e usava o raio do World. Só atualiza se ainda estiver assim
        // (ou seja, se você não mexeu); um valor ajustado por você é mantido.
        private static void MigrateForest01(BiomeProfile biome)
        {
            if (biome == null || biome.viewRadius != 0 || !Mathf.Approximately(biome.fogDensity, 0.020f)) return;

            biome.viewRadius = 3;
            biome.fogDensity = 0.024f;
            EditorUtility.SetDirty(biome);
            AssetDatabase.SaveAssets();
        }

        // Floresta_02 (antiga Floresta): árvores secas, chão escuro, neblina densa e esverdeada, pouca luz.
        private static void ConfigureForest02(BiomeProfile biome)
        {
            biome.lowColor = new Color(0.07f, 0.08f, 0.05f);
            biome.grassColor = new Color(0.12f, 0.16f, 0.08f);
            biome.highColor = new Color(0.20f, 0.19f, 0.14f);
            biome.rockColor = new Color(0.18f, 0.18f, 0.18f);
            biome.fogColor = new Color(0.20f, 0.24f, 0.22f);
            biome.fogDensity = 0.030f;
            biome.ambientColor = new Color(0.14f, 0.16f, 0.16f);
            biome.lightColor = new Color(0.55f, 0.62f, 0.68f);
            biome.lightIntensity = 0.5f;
        }

        private static void EnsureFolder(string parent, string folderName)
        {
            if (!AssetDatabase.IsValidFolder($"{parent}/{folderName}")) AssetDatabase.CreateFolder(parent, folderName);
        }
    }
}
