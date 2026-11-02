using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
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

        // Afundamento fixo, em metros por unidade de escala. Árvores secas: o valor de sempre. Árvores folhosas: 0, porque a
        // altura vem inteira do grupo de cada tipo (ScatterProfile.groups, valores absolutos medidos em metros).
        private const float DeadTreesSinkDepth = 0.1f;
        private const float LeafyTreesSinkDepth = 0f;

        private const string DryTreesFolder ="Assets/Dry_Trees/Prefab";
        private const string MobileFvRoot = "Assets/Mobile_FV";
        private const string MiscFolder = "Assets/Mobile_FV/Prefabs/Misc";
        private const string LeafyTreesAssetPath = "Assets/Project/Scatter/ArvoresMobile.asset";
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
                profile.sinkDepth = DeadTreesSinkDepth;
                profile.collision = ScatterCollision.Trunk;
                profile.trunkRadius = 0.4f;
            });

            // Árvores folhosas da Floresta_01: lista de variações organizada por tipo (ver RebuildTreeGroups).
            ScatterProfile leafyTrees = GetOrCreate("ArvoresMobile", profile =>
            {
                profile.salt = 17;
                profile.attemptsPerChunk = LeafyTreesAttempts;
                profile.minSpacing = 3.5f;
                profile.clustering = 0.25f;
                profile.clusterSize = 70f;
                profile.maxSlope = 28f;
                profile.minScale = 0.9f;
                profile.maxScale = 1.5f;
                profile.sinkDepth = LeafyTreesSinkDepth;
                profile.collision = ScatterCollision.Trunk;
                profile.trunkRadius = 0.3f;
            });

            // Só monta os grupos se o perfil ainda não tem: valores que você ajustou (altura, colisor) nunca são sobrescritos.
            if (!leafyTrees.HasGroups) RebuildTreeGroups(leafyTrees);

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
            EnsureCategory(deadTrees, "trees", "Árvores");
            EnsureCategory(leafyTrees, "trees", "Árvores");
            EnsureCategory(shrubs, "shrubs", "Arbustos");

            // Migração de perfis criados antes: sem versões de neve e com a densidade nova. A densidade só muda se
            // ainda for um dos valores que este script já usou; se você ajustou à mão, fica como está.
            RemoveSnowPrefabs(leafyTrees);
            RemoveSnowPrefabs(shrubs);
            MigrateAttempts(leafyTrees, LeafyTreesAttempts, 30, 18);
            MigrateAttempts(shrubs, ShrubsAttempts, 40);

            ScatterProfile rocks = GetOrCreate("Pedras", profile => ConfigureRocks(profile, attempts: 8, salt: 23));

            // Metade da densidade de Pedras, com outro salt para as posições não coincidirem com as do Campo.
            ScatterProfile sparseRocks = GetOrCreate("PedrasEsparsas", profile => ConfigureRocks(profile, attempts: 4, salt: 24));
            EnsureCategory(rocks, "rocks", "Rochas");
            EnsureCategory(sparseRocks, "rocks", "Rochas");

            // Afundamento: valores que este script já usou (0,1 e 0,03) viram o atual; ajustes seus ficam.
            MigrateSinkDepth(deadTrees, DeadTreesSinkDepth, 0.03f);
            MigrateSinkDepth(leafyTrees, LeafyTreesSinkDepth, 0.1f, 0.03f);

            // Listas anteriores montadas por este script: se o bioma ainda tiver uma delas, é seguro trocar.
            SetList(meadow, new[] { rocks },
                new[] { "ArvoresFolhosas", "Pedras" });
            SetList(forest01, new[] { leafyTrees, shrubs });
            SetList(forest02, new[] { deadTrees, sparseRocks },
                new[] { "ArvoresSecas", "Pedras" });
        }

        // ---- Árvores folhosas organizadas por tipo (Floresta_01) ----

        private const string SpiralTreeId = "SpiralTree";

        // true: usa a versão "Opt_" (1 malha + LOD) quando existe. Os prefabs de Prefabs/<estação> não têm LOD e vários são feitos
        // de dezenas de malhas (Curved: 78), o que pesa no celular; só os tipos sem versão otimizada (Tree, young2, Jungle1)
        // vêm de lá. Em false, todos vêm de Prefabs/ (6 variações por tipo e pacote, em vez de 3).
        private const bool PreferOptimized = true;

        // Tipo (trecho do nome do prefab) e altura inicial em metros em relação ao terreno, medida no Editor. Colisor começa em 1.
        // 'young1' não está na lista: os prefabs eram idênticos aos de 'Tree' (mesma malha e materiais) e foram apagados.
        private static readonly (string id, float elevation)[] TreeTypes =
        {
            ("5Arms", -1.0f), ("Curved", -0.7f), ("FullTree", -0.3f), ("SkinnyBunches", -0.5f), ("SkinnyTall", -0.3f),
            ("TallThin", -1.0f), ("Tree_short", -0.5f), ("Tree_single", -1.5f), ("Tree", -0.1f),
            ("young2", -0.1f), ("Jungle1", -10.0f), (SpiralTreeId, -1.0f)
        };

        // Pacotes de estação usados; Winter, Uniques e New Branches ficam de fora de propósito.
        private static readonly string[] SeasonPacks = { "Autumn", "Spring", "Summer" };

        // Ex.: "Sm1_young1_v2 1", "Opt_S1_5Arms_v3_00", "A2_Tree_short_v1_00". O tipo vem entre o prefixo do pacote e "_v<n>".
        private static readonly Regex TreeNameRegex = new Regex(
            @"^(?:Opt_)?[A-Za-z]+\d_(?<type>" + BuildTypePattern() + @")_v\d",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static string BuildTypePattern()
        {
            List<string> ids = new List<string>();
            foreach (var type in TreeTypes)
            {
                if (type.id != SpiralTreeId) ids.Add(Regex.Escape(type.id));
            }

            return string.Join("|", ids);
        }

        [MenuItem("PROCEDURAL_GAME/Rebuild Tree Groups")]
        public static void RebuildTreeGroupsMenu()
        {
            ScatterProfile profile = AssetDatabase.LoadAssetAtPath<ScatterProfile>(LeafyTreesAssetPath);
            if (profile == null)
            {
                Debug.LogError($"Perfil '{LeafyTreesAssetPath}' não existe. Rode PROCEDURAL_GAME > Create Procedural World primeiro.");
                return;
            }

            RebuildTreeGroups(profile);
        }

        /// <summary>
        /// Refaz os grupos de tipos de árvore do perfil a partir dos pacotes. Altura e colisor já existentes (por id do tipo)
        /// são mantidos; tipos novos entram com os valores iniciais de <see cref="TreeTypes"/>.
        /// </summary>
        public static void RebuildTreeGroups(ScatterProfile profile)
        {
            Dictionary<string, ScatterProfile.PrefabGroup> previous = new Dictionary<string, ScatterProfile.PrefabGroup>();
            if (profile.groups != null)
            {
                foreach (ScatterProfile.PrefabGroup group in profile.groups)
                {
                    if (group != null && !string.IsNullOrEmpty(group.id)) previous[group.id] = group;
                }
            }

            List<string> ignored = new List<string>();
            Dictionary<string, Dictionary<string, List<GameObject>>> optimizedByPack = new Dictionary<string, Dictionary<string, List<GameObject>>>();
            Dictionary<string, Dictionary<string, List<GameObject>>> regularByPack = new Dictionary<string, Dictionary<string, List<GameObject>>>();

            foreach (string pack in SeasonPacks)
            {
                optimizedByPack[pack] = ClassifyTreeFolder($"{MobileFvRoot}/Optimized/{pack}", ignored);
                regularByPack[pack] = ClassifyTreeFolder($"{MobileFvRoot}/Prefabs/{pack}", ignored);
            }

            List<ScatterProfile.PrefabGroup> result = new List<ScatterProfile.PrefabGroup>();
            List<string> report = new List<string>();

            foreach (var treeType in TreeTypes)
            {
                string id = treeType.id;
                float defaultElevation = treeType.elevation;
                List<ScatterProfile.GroupVariant> variants = new List<ScatterProfile.GroupVariant>();
                List<string> origin = new List<string>();

                if (id == SpiralTreeId)
                {
                    foreach (GameObject prefab in FindPrefabs(name => name.StartsWith(SpiralTreeId, StringComparison.Ordinal), MiscFolder))
                    {
                        variants.Add(new ScatterProfile.GroupVariant { prefab = prefab, pack = "Misc" });
                    }

                    origin.Add($"Misc {variants.Count}");
                }
                else
                {
                    foreach (string pack in SeasonPacks)
                    {
                        List<GameObject> source = null;
                        string kind = "Prefabs";

                        if (PreferOptimized && optimizedByPack[pack].TryGetValue(id, out List<GameObject> optimized) && optimized.Count > 0)
                        {
                            source = optimized;
                            kind = "Opt";
                        }
                        else if (regularByPack[pack].TryGetValue(id, out List<GameObject> regular))
                        {
                            source = regular;
                        }

                        if (source == null) continue;

                        foreach (GameObject prefab in source) variants.Add(new ScatterProfile.GroupVariant { prefab = prefab, pack = pack });
                        origin.Add($"{pack} {source.Count} [{kind}]");
                    }
                }

                if (variants.Count == 0)
                {
                    report.Add($"{id}: nenhum prefab encontrado");
                    continue;
                }

                bool had = previous.TryGetValue(id, out ScatterProfile.PrefabGroup old);
                result.Add(new ScatterProfile.PrefabGroup
                {
                    id = id,
                    displayName = id,
                    elevation = had ? old.elevation : defaultElevation,
                    colliderScale = had ? old.colliderScale : 1f,
                    weight = had && old.weight > 0f ? old.weight : 1f,
                    variants = variants.ToArray()
                });

                report.Add($"{id}: {variants.Count} variações ({string.Join(", ", origin)})");
            }

            profile.groups = result.ToArray();
            profile.prefabs = Array.Empty<GameObject>();
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            string message = $"Grupos de árvores de '{profile.name}' refeitos ({result.Count} tipos):\n" + string.Join("\n", report);
            if (ignored.Count > 0) message += $"\nIgnorados (fora dos tipos usados): {string.Join(", ", ignored)}";
            Debug.Log(message);
        }

        /// <summary>Agrupa os prefabs de uma pasta pelo tipo de árvore (id canônico de <see cref="TreeTypes"/>); o que não casa vai para <paramref name="ignored"/>.</summary>
        private static Dictionary<string, List<GameObject>> ClassifyTreeFolder(string folder, List<string> ignored)
        {
            Dictionary<string, List<GameObject>> result = new Dictionary<string, List<GameObject>>();
            if (!AssetDatabase.IsValidFolder(folder)) return result;

            List<string> paths = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder })) paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            paths.Sort(string.CompareOrdinal);

            foreach (string path in paths)
            {
                if (IsSnowAsset(path)) continue;

                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                Match match = TreeNameRegex.Match(prefab.name);
                if (!match.Success)
                {
                    ignored.Add(prefab.name);
                    continue;
                }

                string id = CanonicalTreeId(match.Groups["type"].Value);
                if (!result.TryGetValue(id, out List<GameObject> list))
                {
                    list = new List<GameObject>();
                    result.Add(id, list);
                }

                list.Add(prefab);
            }

            return result;
        }

        private static string CanonicalTreeId(string typeFromName)
        {
            foreach (var type in TreeTypes)
            {
                if (string.Equals(type.id, typeFromName, StringComparison.OrdinalIgnoreCase)) return type.id;
            }

            return typeFromName;
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

        // Id (ASCII, vira chave de gravação) e nome mostrado são preenchidos separados e só se vazios: o que você mudou fica.
        private static void EnsureCategory(ScatterProfile profile, string categoryId, string displayName)
        {
            bool changed = false;

            if (string.IsNullOrEmpty(profile.categoryId))
            {
                profile.categoryId = categoryId;
                changed = true;
            }

            if (string.IsNullOrEmpty(profile.category))
            {
                profile.category = displayName;
                changed = true;
            }

            if (!changed) return;

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
        }

        private static void MigrateSinkDepth(ScatterProfile profile, float newValue, params float[] previousDefaults)
        {
            bool wasDefault = false;
            foreach (float previous in previousDefaults) wasDefault |= Mathf.Approximately(profile.sinkDepth, previous);
            if (!wasDefault) return;

            profile.sinkDepth = newValue;
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
