using System;
using System.Collections.Generic;
using PROCEDURAL_GAME.World;
using UnityEditor;
using UnityEngine;

namespace PROCEDURAL_GAME.EditorTools
{
    /// <summary>
    /// Cria os perfis de objetos padrão em Assets/Project/Scatter, já apontando para os pacotes importados,
    /// e monta a lista de objetos de cada bioma:
    /// - Campo: só pedras;
    /// - Floresta_01: árvores variadas do Mobile_FV (bem densa) + arbustos, sem pedras;
    /// - Floresta_02: árvores secas (Dry_Trees) + pedras em metade da densidade do Campo, sem arbustos.
    /// Chamado pelo menu Create Procedural World. Idempotente: não sobrescreve perfis existentes, e só troca a
    /// lista de um bioma se ela estiver vazia ou ainda for a que este script montava antes (ajustes seus ficam).
    /// </summary>
    public static class ScatterBuilder
    {
        private const string ScatterFolder = "Assets/Project/Scatter";

        // Tentativas por chunk da Floresta_01 (a densidade de cada tipo é ajustada separadamente, em seu próprio perfil).
        // Árvores: 9 (já foram 30 e depois 18). Arbustos: 20 (já foram 40).
        private const int LeafyTreesAttempts = 9;
        private const int ShrubsAttempts = 20;

        private const string DryTreesFolder ="Assets/Dry_Trees/Prefab";
        private const string LeafyTreesSummerFolder = "Assets/Mobile_FV/Optimized/Summer";
        private const string LeafyTreesSpringFolder = "Assets/Mobile_FV/Optimized/Spring";
        private const string GroundCoverFolder = "Assets/Mobile_FV/Prefabs/GroundCover";
        private const string RocksFolder = "Assets/Pizza&Games/Realistic Rocks/Prefabs";

        public static void EnsureDefaults(BiomeProfile meadow, BiomeProfile forest01, BiomeProfile forest02)
        {
            ScatterProfile deadTrees = GetOrCreate("ArvoresSecas", profile =>
            {
                profile.prefabs = FindPrefabs(name => true, DryTreesFolder);
                profile.salt = 11;
                profile.attemptsPerChunk = 14;
                profile.minSpacing = 6f;
                profile.clustering = 0.6f;
                profile.clusterSize = 60f;
                profile.maxSlope = 30f;
                profile.minScale = 0.8f;
                profile.maxScale = 1.3f;
                profile.collision = ScatterCollision.Trunk;
                profile.trunkRadius = 0.4f;
            });

            // Todos os tipos de árvore de verão e primavera (copas, formas e altura diferentes) para a floresta ser variada.
            // Outono e as "Uniques" do pacote também existem; basta arrastá-los para a lista no Inspector do perfil.
            ScatterProfile leafyTrees = GetOrCreate("ArvoresMobile", profile =>
            {
                profile.prefabs = FindPrefabs(name => name.StartsWith("Opt_"), LeafyTreesSummerFolder, LeafyTreesSpringFolder);
                profile.salt = 17;
                profile.attemptsPerChunk = LeafyTreesAttempts;
                profile.minSpacing = 3.5f;
                profile.clustering = 0.25f;
                profile.clusterSize = 70f;
                profile.maxSlope = 28f;
                profile.minScale = 0.9f;
                profile.maxScale = 1.5f;
                profile.collision = ScatterCollision.Trunk;
                profile.trunkRadius = 0.3f;
            });

            // Arbustos: samambaias e touceiras do pacote (verão). Sem colisão: o Player passa por dentro.
            ScatterProfile shrubs = GetOrCreate("Arbustos", profile =>
            {
                profile.prefabs = FindPrefabs(
                    name => name.StartsWith("Sm") && (name.Contains("fern") || name.Contains("WeedGroup")), GroundCoverFolder);
                profile.salt = 31;
                profile.attemptsPerChunk = ShrubsAttempts;
                profile.minSpacing = 1.5f;
                profile.clustering = 0.5f;
                profile.clusterSize = 25f;
                profile.maxSlope = 40f;
                profile.minScale = 0.8f;
                profile.maxScale = 1.5f;
                profile.sinkDepth = 0.05f;
                profile.collision = ScatterCollision.None;
            });

            // Cada perfil entra numa categoria do menu de Desempenho. Só preenche se vazio: um nome que você mudou fica.
            EnsureCategory(deadTrees, "Árvores");
            EnsureCategory(leafyTrees, "Árvores");
            EnsureCategory(shrubs, "Arbustos");

            // Migração de perfis criados antes: sem versões de neve e com a densidade nova. A densidade só muda se
            // ainda for um dos valores que este script já usou; se você ajustou à mão, fica como está.
            RemoveSnowPrefabs(leafyTrees);
            RemoveSnowPrefabs(shrubs);
            MigrateAttempts(leafyTrees, LeafyTreesAttempts, 30, 18);
            MigrateAttempts(shrubs, ShrubsAttempts, 40);

            ScatterProfile rocks = GetOrCreate("Pedras", profile => ConfigureRocks(profile, attempts: 8, salt: 23));

            // Metade da densidade de Pedras, com outro salt para as posições não coincidirem com as do Campo.
            ScatterProfile sparseRocks = GetOrCreate("PedrasEsparsas", profile => ConfigureRocks(profile, attempts: 4, salt: 24));
            EnsureCategory(rocks, "Rochas");
            EnsureCategory(sparseRocks, "Rochas");

            // Listas anteriores montadas por este script: se o bioma ainda tiver uma delas, é seguro trocar.
            SetList(meadow, new[] { rocks },
                new[] { "ArvoresFolhosas", "Pedras" });
            SetList(forest01, new[] { leafyTrees, shrubs });
            SetList(forest02, new[] { deadTrees, sparseRocks },
                new[] { "ArvoresSecas", "Pedras" });
        }

        private static void ConfigureRocks(ScatterProfile profile, int attempts, int salt)
        {
            // Sem as versões com musgo por enquanto: o dobro de materiais para o mesmo formato.
            profile.prefabs = FindPrefabs(name => !name.Contains("Moss"), RocksFolder);
            profile.salt = salt;
            profile.attemptsPerChunk = attempts;
            profile.minSpacing = 3f;
            profile.clustering = 0.4f;
            profile.clusterSize = 45f;
            profile.maxSlope = 50f;
            profile.minScale = 0.8f;
            profile.maxScale = 1.6f;
            profile.alignToSurface = 0.6f;
            profile.sinkDepth = 0.15f;
            profile.collision = ScatterCollision.Box;
        }

        private static ScatterProfile GetOrCreate(string profileName, Action<ScatterProfile> configure)
        {
            string path = $"{ScatterFolder}/{profileName}.asset";
            ScatterProfile profile = AssetDatabase.LoadAssetAtPath<ScatterProfile>(path);
            if (profile != null) return profile;

            if (!AssetDatabase.IsValidFolder(ScatterFolder)) AssetDatabase.CreateFolder("Assets/Project", "Scatter");

            profile = ScriptableObject.CreateInstance<ScatterProfile>();
            configure(profile);
            if (profile.prefabs == null || profile.prefabs.Length == 0)
            {
                Debug.LogWarning($"Perfil '{profileName}': nenhum prefab encontrado (pasta movida ou renomeada?). Arraste os prefabs no Inspector do asset.");
            }

            AssetDatabase.CreateAsset(profile, path);
            AssetDatabase.SaveAssets();
            return profile;
        }

        private static GameObject[] FindPrefabs(Func<string, bool> nameFilter, params string[] folders)
        {
            List<GameObject> result = new List<GameObject>();

            List<string> existing = new List<string>();
            foreach (string folder in folders)
            {
                if (AssetDatabase.IsValidFolder(folder)) existing.Add(folder);
            }

            if (existing.Count == 0) return result.ToArray();

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", existing.ToArray()))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                // A busca desce nas subpastas: sem isso entram as versões de neve (folhas brancas) do Mobile_FV.
                if (IsSnowAsset(path)) continue;

                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null && nameFilter(prefab.name)) result.Add(prefab);
            }

            // FindAssets não garante ordem; ordenar mantém o resultado estável entre máquinas.
            result.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return result.ToArray();
        }

        private static void EnsureCategory(ScatterProfile profile, string category)
        {
            if (!string.IsNullOrEmpty(profile.category)) return;

            profile.category = category;
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
        }

        private static void MigrateAttempts(ScatterProfile profile, int newValue, params int[] previousDefaults)
        {
            if (profile.attemptsPerChunk == newValue || Array.IndexOf(previousDefaults, profile.attemptsPerChunk) < 0) return;

            profile.attemptsPerChunk = newValue;
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
        }

        private static bool IsSnowAsset(string path)
        {
            return path.IndexOf("snow", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Perfis já criados em execuções anteriores guardam seus prefabs; esta limpeza tira deles os de neve.
        /// Só remove essas entradas: o resto da lista (inclusive o que você acrescentou) fica.
        /// </summary>
        private static void RemoveSnowPrefabs(ScatterProfile profile)
        {
            if (profile.prefabs == null) return;

            List<GameObject> kept = new List<GameObject>();
            foreach (GameObject prefab in profile.prefabs)
            {
                if (prefab == null || !IsSnowAsset(AssetDatabase.GetAssetPath(prefab))) kept.Add(prefab);
            }

            if (kept.Count == profile.prefabs.Length) return;

            profile.prefabs = kept.ToArray();
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Troca a lista de objetos do bioma por <paramref name="profiles"/> se ela estiver vazia ou for igual a uma das
        /// listas antigas (nomes dos perfis, na ordem). Qualquer outra lista é considerada edição sua e é mantida.
        /// </summary>
        private static void SetList(BiomeProfile biome, ScatterProfile[] profiles, params string[][] legacyLists)
        {
            if (biome == null) return;

            SerializedObject serialized = new SerializedObject(biome);
            SerializedProperty list = serialized.FindProperty("scatter");

            if (list.arraySize > 0 && !MatchesAny(list, legacyLists)) return;

            list.arraySize = profiles.Length;
            for (int i = 0; i < profiles.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = profiles[i];

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(biome);
            AssetDatabase.SaveAssets();
        }

        private static bool MatchesAny(SerializedProperty list, string[][] legacyLists)
        {
            foreach (string[] legacy in legacyLists)
            {
                if (legacy.Length != list.arraySize) continue;

                bool same = true;
                for (int i = 0; i < legacy.Length && same; i++)
                {
                    UnityEngine.Object item = list.GetArrayElementAtIndex(i).objectReferenceValue;
                    same = item != null && item.name == legacy[i];
                }

                if (same) return true;
            }

            return false;
        }
    }
}
