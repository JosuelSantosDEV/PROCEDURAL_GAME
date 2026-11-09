using System.Collections.Generic;
using PROCEDURAL_GAME.Core;
using PROCEDURAL_GAME.World;
using UnityEditor;
using UnityEngine;

namespace PROCEDURAL_GAME.EditorTools
{
    /// <summary>
    /// Prepara a comida da Floresta_01 a partir dos pacotes "Otoh Art" e "PBR_Watermelon": converte os materiais para o URP (eram do shader
    /// Standard, que aparece rosa), limita as texturas no celular, cria prefabs próprios das frutas (tamanho de jogo e pivô no chão) em
    /// Assets/Project/Pickups/Food e o perfil Comida, que nasce só nas zonas Summer. Pode rodar de novo: refaz os prefabs e mantém os
    /// números que você mudou no perfil.
    /// </summary>
    public static class FoodBuilder
    {
        private const string BiomePath = "Assets/Project/Biomes/Floresta_01.asset";
        private const string TreesProfilePath = "Assets/Project/Scatter/ArvoresMobile.asset";
        private const string ProfilePath = "Assets/Project/Scatter/Comida.asset";
        private const string PickupsFolder = "Assets/Project/Pickups";
        private const string FoodFolder = PickupsFolder + "/Food";
        private const string FoodPack = "Food";
        private const string FoodZone = "Summer";
        private const int MobileMaxTextureSize = 512;

        private static readonly string[] MaterialFolders = { "Assets/Otoh Art/Materials", "Assets/PBR_Watermelon/Material" };

        private static readonly string[] Textures =
        {
            "Assets/Otoh Art/Materials/apple-base.png",
            "Assets/Otoh Art/Materials/orange-base.png",
            "Assets/Otoh Art/Materials/orange-bump-map.png",
            "Assets/Otoh Art/Materials/pear-base.png",
            "Assets/PBR_Watermelon/textures/512/Watermelon_512_Albedo.png",
            "Assets/PBR_Watermelon/textures/512/Watermelon_512_Normal.png"
        };

        // Maior medida de cada fruta, em metros. Maior que o real (o Player tem 2x a escala humana e a fruta precisa ser vista na vegetação).
        private static readonly (string id, string source, float size, float weight)[] Fruits =
        {
            ("Apple", "Assets/Otoh Art/Prefabs/apple.prefab", 0.45f, 1f),
            ("Orange", "Assets/Otoh Art/Prefabs/orange.prefab", 0.45f, 1f),
            ("Pear", "Assets/Otoh Art/Prefabs/pear.prefab", 0.5f, 1f),
            ("Watermelon", "Assets/PBR_Watermelon/prefabs/512/Watermelon_512.prefab", 0.9f, 0.5f)
        };

        [MenuItem("PROCEDURAL_GAME/Food/Set Up Food in Floresta_01")]
        public static void Build()
        {
            BiomeProfile biome = AssetDatabase.LoadAssetAtPath<BiomeProfile>(BiomePath);
            ScatterProfile trees = AssetDatabase.LoadAssetAtPath<ScatterProfile>(TreesProfilePath);
            if (biome == null || trees == null)
            {
                Debug.LogError($"'{BiomePath}' ou '{TreesProfilePath}' não existe. Rode PROCEDURAL_GAME > Create Procedural World primeiro.");
                return;
            }

            if (!AddFoodToSummer(trees)) return;

            int converted = ConvertMaterials();
            ConfigureTextures();

            List<ScatterProfile.PrefabGroup> groups = BuildPrefabs();
            if (groups.Count == 0)
            {
                Debug.LogError("Nenhuma fruta encontrada: confira os pacotes 'Otoh Art' e 'PBR_Watermelon' em Assets.");
                return;
            }

            ScatterProfile profile = EnsureProfile(trees);
            profile.groups = groups.ToArray();
            EditorUtility.SetDirty(profile);

            AddToBiome(biome, profile);

            AssetDatabase.SaveAssets();
            Debug.Log($"Comida pronta: {groups.Count} frutas em '{FoodFolder}', perfil '{ProfilePath}' na Floresta_01, só nas zonas {FoodZone}. {converted} material(is) convertido(s) para o URP.");
        }

        private static bool AddFoodToSummer(ScatterProfile trees)
        {
            if (trees.zones != null)
            {
                foreach (ScatterProfile.SeasonZone zone in trees.zones)
                {
                    if (zone == null || zone.id != FoodZone) continue;

                    List<string> packs = new List<string>(zone.packs ?? new string[0]);
                    if (!packs.Contains(FoodPack))
                    {
                        packs.Add(FoodPack);
                        zone.packs = packs.ToArray();
                        EditorUtility.SetDirty(trees);
                    }

                    return true;
                }
            }

            Debug.LogError($"O perfil '{trees.name}' não tem a zona '{FoodZone}'. Rode PROCEDURAL_GAME > Create Procedural World primeiro.");
            return false;
        }

        private static int ConvertMaterials()
        {
            int count = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", MaterialFolders))
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (material != null && MaterialUpgrader.ConvertIfNeeded(material)) count++;
            }

            return count;
        }

        // Frutas são pequenas na tela: 512 px bastam no celular. O arquivo original não muda.
        private static void ConfigureTextures()
        {
            foreach (string path in Textures)
            {
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;

                bool changed = false;
                foreach (string platform in new[] { "Android", "iOS" })
                {
                    TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(platform);
                    if (settings.overridden && settings.maxTextureSize == MobileMaxTextureSize && settings.format == TextureImporterFormat.ASTC_6x6) continue;

                    settings.overridden = true;
                    settings.maxTextureSize = MobileMaxTextureSize;
                    settings.format = TextureImporterFormat.ASTC_6x6;
                    importer.SetPlatformTextureSettings(settings);
                    changed = true;
                }

                if (changed) importer.SaveAndReimport();
            }
        }

        private static List<ScatterProfile.PrefabGroup> BuildPrefabs()
        {
            EnsureFolder("Assets/Project", "Pickups");
            EnsureFolder(PickupsFolder, "Food");

            List<ScatterProfile.PrefabGroup> groups = new List<ScatterProfile.PrefabGroup>();

            foreach (var fruit in Fruits)
            {
                GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(fruit.source);
                if (source == null)
                {
                    Debug.LogWarning($"Fruta '{fruit.source}' não encontrada: fica de fora.");
                    continue;
                }

                GameObject prefab = BuildFruitPrefab(fruit.id, source, fruit.size);
                groups.Add(new ScatterProfile.PrefabGroup
                {
                    id = fruit.id,
                    displayName = fruit.id,
                    weight = fruit.weight,
                    variants = new[] { new ScatterProfile.GroupVariant { prefab = prefab, pack = FoodPack } }
                });
            }

            return groups;
        }

        // Raiz vazia no chão com a fruta dentro, já no tamanho de jogo e apoiada em y = 0: o espalhador coloca o pivô sobre o terreno.
        private static GameObject BuildFruitPrefab(string id, GameObject source, float size)
        {
            GameObject root = new GameObject(id);
            GameObject model = Object.Instantiate(source, root.transform);
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;

            Bounds bounds = MeasureBounds(model);
            float largest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            if (largest > 0f) model.transform.localScale *= size / largest;

            bounds = MeasureBounds(model);
            model.transform.position -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, $"{FoodFolder}/{id}.prefab");
            Object.DestroyImmediate(root);
            return saved;
        }

        private static Bounds MeasureBounds(GameObject target)
        {
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(target.transform.position, Vector3.zero);

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private static ScatterProfile EnsureProfile(ScatterProfile trees)
        {
            ScatterProfile profile = AssetDatabase.LoadAssetAtPath<ScatterProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<ScatterProfile>();
                profile.salt = 41;
                profile.attemptsPerChunk = 1f;
                profile.minSpacing = 8f;
                profile.clustering = 0.3f;
                profile.clusterSize = 40f;
                profile.maxSlope = 28f;
                profile.minScale = 0.9f;
                profile.maxScale = 1.1f;
                profile.alignToSurface = 0.6f;
                profile.sinkDepth = 0f;
                profile.collision = ScatterCollision.None;
                profile.castShadowsFromLevel = PerformanceSettings.ShadowMedium;
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }

            // Mesmo mapa de zonas das árvores: a fruta nasce onde as árvores são de verão.
            profile.zoneSource = trees;
            profile.pickupKind = PickupKind.Food;
            profile.essential = true;
            return profile;
        }

        private static void AddToBiome(BiomeProfile biome, ScatterProfile profile)
        {
            List<ScatterProfile> list = new List<ScatterProfile>(biome.scatter ?? new ScatterProfile[0]);
            if (list.Contains(profile)) return;

            list.Add(profile);
            biome.scatter = list.ToArray();
            EditorUtility.SetDirty(biome);
        }

        private static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder($"{parent}/{name}")) AssetDatabase.CreateFolder(parent, name);
        }
    }
}
