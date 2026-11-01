using PROCEDURAL_GAME.Core;
using PROCEDURAL_GAME.DebugTools;
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
            BiomeProfile forest = GetOrCreateBiome("Floresta", ConfigureForest);

            WorldGenerator generator = Object.FindObjectOfType<WorldGenerator>();
            if (generator == null)
            {
                GameObject created = new GameObject("World", typeof(WorldGenerator));
                Undo.RegisterCreatedObjectUndo(created, UndoName);
                generator = created.GetComponent<WorldGenerator>();
            }

            GameObject world = generator.gameObject;
            if (world.GetComponent<AtmosphereController>() == null) Undo.AddComponent<AtmosphereController>(world);
            if (world.GetComponent<FrameRateController>() == null) Undo.AddComponent<FrameRateController>(world);
            if (world.GetComponent<FpsCounter>() == null) Undo.AddComponent<FpsCounter>(world);

            // Só preenche campos vazios: se você já trocou o perfil ou o material no Inspector, isso é mantido.
            SerializedObject serialized = new SerializedObject(generator);
            AssignIfEmpty(serialized, "terrainMaterial", material);
            AssignIfEmpty(serialized, "biome", forest != null ? forest : meadow);
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
            Debug.Log("Mundo procedural pronto. Salve a cena (Ctrl+S) e dê Play. Para comparar visuais, troque o campo 'Biome' do objeto World entre Campo e Floresta.");
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

        // Floresta sombria: chão escuro, neblina densa e esverdeada, pouca luz.
        private static void ConfigureForest(BiomeProfile biome)
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
