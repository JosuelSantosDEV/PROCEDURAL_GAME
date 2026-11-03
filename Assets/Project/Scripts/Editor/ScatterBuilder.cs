using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using PROCEDURAL_GAME.Core;
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
        private const float LeafyTreesAttempts = 5.4f;
        private const float ShrubsAttempts = 10f;

        // Afundamento fixo, em metros por unidade de escala. Árvores secas: o valor de sempre. Árvores folhosas: 0, porque a
        // altura vem inteira do grupo de cada tipo (ScatterProfile.groups, valores absolutos medidos em metros).
        private const float DeadTreesSinkDepth = 0.1f;
        private const float LeafyTreesSinkDepth = 0f;

        private const string DryTreesFolder ="Assets/Dry_Trees/Prefab";

        // Árvores e vegetação otimizadas (geradas por Trees > 3 - Build ALL a partir do Mobile_FV): 1 renderer + LODGroup.
        private const string TreesRoot = "Assets/Project/Trees";
        private const string MiscFolder = TreesRoot + "/Misc";
        private const string LegacyRoot = "Assets/Mobile_FV/";
        private const string LeafyTreesAssetPath = "Assets/Project/Scatter/ArvoresMobile.asset";
        private const string ShrubsAssetPath = "Assets/Project/Scatter/Arbustos.asset";
        private const string GroundCoverFolder = TreesRoot + "/GroundCover";
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
                profile.spacingByDensity = true;
                profile.spacingJitter = 0.15f;
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
                profile.spacingByDensity = true;
                profile.mixByPreference = true;
            });

            // Só monta os grupos se o perfil ainda não tem: valores que você ajustou (altura, colisor) nunca são sobrescritos.
            // Perfis ainda apontando para os prefabs originais do Mobile_FV passam para as versões otimizadas (com LOD).
            if (!leafyTrees.HasGroups || UsesLegacyPrefabs(leafyTrees)) RebuildTreeGroups(leafyTrees);
            EnsureSeasonZones(leafyTrees);

            // Arbustos: samambaias e touceiras, divididos em Verde e Seco (ver RebuildShrubGroups) e seguindo as zonas das árvores.
            // Sem colisão: o Player passa por dentro.
            ScatterProfile shrubs = GetOrCreate("Arbustos", profile =>
            {
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

            // Só monta os grupos se o perfil ainda não tem (ajustes seus ficam) ou se ainda aponta para os prefabs originais.
            if (!shrubs.HasGroups || UsesLegacyPrefabs(shrubs)) RebuildShrubGroups(shrubs);

            if (shrubs.zoneSource == null)
            {
                shrubs.zoneSource = leafyTrees;
                EditorUtility.SetDirty(shrubs);
                AssetDatabase.SaveAssets();
            }

            // Cada perfil entra numa categoria do menu de Desempenho. Só preenche se vazio: um nome que você mudou fica.
            EnsureCategory(deadTrees, "trees", "Árvores");
            EnsureCategory(leafyTrees, "trees", "Árvores");
            EnsureCategory(shrubs, "shrubs", "Arbustos");

            // Migração de perfis criados antes: sem versões de neve e com a densidade nova. A densidade só muda se
            // ainda for um dos valores que este script já usou; se você ajustou à mão, fica como está.
            RemoveSnowPrefabs(leafyTrees);
            RemoveSnowPrefabs(shrubs);
            MigrateAttempts(leafyTrees, LeafyTreesAttempts, 30, 18, 9);
            MigrateAttempts(shrubs, ShrubsAttempts, 40, 20);

            ScatterProfile rocks = GetOrCreate("Pedras", profile => ConfigureRocks(profile, attempts: 8, salt: 23));

            // Metade da densidade de Pedras, com outro salt para as posições não coincidirem com as do Campo.
            ScatterProfile sparseRocks = GetOrCreate("PedrasEsparsas", profile => ConfigureRocks(profile, attempts: 4, salt: 24));
            EnsureCategory(rocks, "rocks", "Rochas");
            EnsureCategory(sparseRocks, "rocks", "Rochas");

            // Sombras: árvores projetam a partir da qualidade Baixa; pedras só da Média; arbustos nunca (são muitos e pequenos).
            // Só preenche se ainda não definido (-1): o que você ajustou no Inspector fica.
            EnsureShadowLevel(deadTrees, PerformanceSettings.ShadowLow);
            EnsureShadowLevel(leafyTrees, PerformanceSettings.ShadowLow);
            EnsureShadowLevel(shrubs, PerformanceSettings.ShadowOff);
            EnsureShadowLevel(rocks, PerformanceSettings.ShadowMedium);
            EnsureShadowLevel(sparseRocks, PerformanceSettings.ShadowMedium);

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
        private const string MiscPack = "Misc";

        // Tipo (trecho do nome do prefab), altura inicial em metros em relação ao terreno, preferência (1 a 3, ver
        // ScatterProfile.mixByPreference) e multiplicador do colisor, medidos no Editor. Só valem para tipos novos no perfil:
        // o que já está gravado no perfil (por id) é mantido.
        // 'young1' não está na lista: os prefabs eram idênticos aos de 'Tree' (mesma malha e materiais) e foram apagados.
        private static readonly (string id, float elevation, int preference, float collider)[] TreeTypes =
        {
            ("5Arms", -1.0f, 3, 4.5f), ("Curved", -0.7f, 2, 1.0f), ("FullTree", -0.3f, 2, 1.0f),
            ("SkinnyBunches", -0.5f, 2, 1.0f), ("SkinnyTall", -0.3f, 1, 1.0f), ("TallThin", -1.0f, 2, 1.2f),
            ("Tree_short", -0.5f, 2, 1.0f), ("Tree_single", -1.5f, 3, 5.0f), ("Tree", -0.1f, 1, 0.1f),
            ("young2", -0.1f, 2, 0.1f), ("Jungle1", -10.0f, 3, 7.0f), (SpiralTreeId, -1.0f, 2, 1.0f)
        };

        // Pacotes de estação usados nesta floresta; Winter não entra por enquanto (existe em Trees/Winter, para depois).
        private static readonly string[] SeasonPacks = { "Autumn", "Spring", "Summer" };

        // Ex.: "Sm1_young2_v2", "A2_Tree_short_v1_00". O tipo vem entre o prefixo do pacote e "_v<n>".
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

        /// <summary>
        /// Zonas de estação da floresta: cada zona do mundo usa árvores de uma única estação. Summer e Misc ficam juntas (são
        /// parecidas). Só preenche se o perfil ainda não tem zonas: o que você mudou no Inspector fica.
        /// </summary>
        private static void EnsureSeasonZones(ScatterProfile profile)
        {
            bool changed = false;

            if (profile.zones == null || profile.zones.Length == 0)
            {
                profile.zones = new[]
                {
                    new ScatterProfile.SeasonZone { id = "Autumn", packs = new[] { "Autumn" } },
                    new ScatterProfile.SeasonZone { id = "Spring", packs = new[] { "Spring" } },
                    new ScatterProfile.SeasonZone { id = "Summer", packs = new[] { "Summer", MiscPack } }
                };
                changed = true;
            }

            // Arbustos: o outono usa os secos; primavera e verão, os verdes. Só acrescenta se ainda não estiver na zona.
            changed |= AddPackToZone(profile, "Autumn", DryShrubPack);
            changed |= AddPackToZone(profile, "Spring", GreenShrubPack);
            changed |= AddPackToZone(profile, "Summer", GreenShrubPack);

            if (!changed) return;

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
        }

        private static bool AddPackToZone(ScatterProfile profile, string zoneId, string pack)
        {
            foreach (ScatterProfile.SeasonZone zone in profile.zones)
            {
                if (zone == null || zone.id != zoneId) continue;

                List<string> packs = new List<string>(zone.packs ?? Array.Empty<string>());
                if (packs.Contains(pack)) return false;

                packs.Add(pack);
                zone.packs = packs.ToArray();
                return true;
            }

            return false;
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

            ScatterProfile shrubs = AssetDatabase.LoadAssetAtPath<ScatterProfile>(ShrubsAssetPath);
            if (shrubs != null) RebuildShrubGroups(shrubs);
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
            Dictionary<string, Dictionary<string, List<GameObject>>> treesByPack = new Dictionary<string, Dictionary<string, List<GameObject>>>();

            foreach (string pack in SeasonPacks) treesByPack[pack] = ClassifyTreeFolder($"{TreesRoot}/{pack}", ignored);

            List<ScatterProfile.PrefabGroup> result = new List<ScatterProfile.PrefabGroup>();
            List<string> report = new List<string>();

            foreach (var treeType in TreeTypes)
            {
                string id = treeType.id;
                float defaultElevation = treeType.elevation;
                int defaultPreference = treeType.preference;
                float defaultCollider = treeType.collider;
                List<ScatterProfile.GroupVariant> variants = new List<ScatterProfile.GroupVariant>();
                List<string> origin = new List<string>();

                if (id == SpiralTreeId)
                {
                    foreach (GameObject prefab in FindPrefabs(name => name.StartsWith(SpiralTreeId, StringComparison.Ordinal), MiscFolder))
                    {
                        variants.Add(new ScatterProfile.GroupVariant { prefab = prefab, pack = MiscPack });
                    }

                    origin.Add($"Misc {variants.Count}");
                }
                else
                {
                    foreach (string pack in SeasonPacks)
                    {
                        if (!treesByPack[pack].TryGetValue(id, out List<GameObject> source)) continue;

                        foreach (GameObject prefab in source) variants.Add(new ScatterProfile.GroupVariant { prefab = prefab, pack = pack });
                        origin.Add($"{pack} {source.Count}");
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
                    colliderScale = had ? old.colliderScale : defaultCollider,
                    weight = had && old.weight > 0f ? old.weight : 1f,
                    preference = had && old.preference >= 1 && old.preference <= 3 ? old.preference : defaultPreference,
                    variants = variants.ToArray()
                });

                report.Add($"{id}: {variants.Count} variações ({string.Join(", ", origin)})");
            }

            if (result.Count == 0)
            {
                Debug.LogError($"Nenhuma árvore encontrada em '{TreesRoot}'. Rode PROCEDURAL_GAME > Trees > 1 e 3 (Build ALL) antes; o perfil '{profile.name}' não foi alterado.");
                return;
            }

            profile.groups = result.ToArray();
            profile.prefabs = Array.Empty<GameObject>();
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            string message = $"Grupos de árvores de '{profile.name}' refeitos ({result.Count} tipos):\n" + string.Join("\n", report);
            if (ignored.Count > 0) message += $"\nIgnorados (fora dos tipos usados): {string.Join(", ", ignored)}";
            Debug.Log(message);
        }

        // ---- Arbustos: Verde e Seco ----

        // Também são os nomes dos pacotes das variações (as zonas de ScatterProfile.zones escolhem por eles).
        private const string GreenShrubPack = "Green";
        private const string DryShrubPack = "Dry";

        /// <summary>
        /// "Dry" para os arbustos do pacote de outono (A1_, A2_: folhagem marrom/amarela/laranja), "Green" para primavera e verão
        /// (S1_, S2_, Sm1_, Sm2_), nulo para o resto. Entram samambaias (fern, fernPlant) e touceiras de capim (grassBunch); ficam
        /// de fora o Winter (ainda sem uso), as plantas soltas (grass, weed) e os WeedGroup (galhos que parecem árvore seca).
        /// </summary>
        private static string ShrubPackOf(string name)
        {
            if (name.Contains("WeedGroup")) return null;
            if (!name.Contains("fern") && !name.Contains("grassBunch")) return null;

            if (name.StartsWith("A1_", StringComparison.Ordinal) || name.StartsWith("A2_", StringComparison.Ordinal)) return DryShrubPack;

            if (name.StartsWith("S1_", StringComparison.Ordinal) || name.StartsWith("S2_", StringComparison.Ordinal)
                || name.StartsWith("Sm1_", StringComparison.Ordinal) || name.StartsWith("Sm2_", StringComparison.Ordinal))
            {
                return GreenShrubPack;
            }

            return null;
        }

        /// <summary>Refaz os grupos "Verde" e "Seco" do perfil de arbustos a partir de Assets/Project/Trees/GroundCover. Altura, colisor e peso já gravados (por id) ficam.</summary>
        public static void RebuildShrubGroups(ScatterProfile profile)
        {
            Dictionary<string, ScatterProfile.PrefabGroup> previous = new Dictionary<string, ScatterProfile.PrefabGroup>();
            if (profile.groups != null)
            {
                foreach (ScatterProfile.PrefabGroup group in profile.groups)
                {
                    if (group != null && !string.IsNullOrEmpty(group.id)) previous[group.id] = group;
                }
            }

            GameObject[] found = FindPrefabs(name => ShrubPackOf(name) != null, GroundCoverFolder);
            if (found.Length == 0)
            {
                Debug.LogError($"Nenhum arbusto encontrado em '{GroundCoverFolder}'. Rode PROCEDURAL_GAME > Trees > 3 - Build ALL antes; o perfil '{profile.name}' não foi alterado.");
                return;
            }

            (string id, string displayName)[] kinds = { (GreenShrubPack, "Verde"), (DryShrubPack, "Seco") };
            List<ScatterProfile.PrefabGroup> result = new List<ScatterProfile.PrefabGroup>();
            List<string> report = new List<string>();

            foreach (var kind in kinds)
            {
                List<ScatterProfile.GroupVariant> variants = new List<ScatterProfile.GroupVariant>();
                foreach (GameObject prefab in found)
                {
                    if (ShrubPackOf(prefab.name) == kind.id) variants.Add(new ScatterProfile.GroupVariant { prefab = prefab, pack = kind.id });
                }

                if (variants.Count == 0)
                {
                    report.Add($"{kind.displayName}: nenhum prefab");
                    continue;
                }

                bool had = previous.TryGetValue(kind.id, out ScatterProfile.PrefabGroup old);
                result.Add(new ScatterProfile.PrefabGroup
                {
                    id = kind.id,
                    displayName = kind.displayName,
                    elevation = had ? old.elevation : 0f,
                    colliderScale = had ? old.colliderScale : 1f,
                    weight = had && old.weight > 0f ? old.weight : 1f,
                    preference = 2,
                    variants = variants.ToArray()
                });

                report.Add($"{kind.displayName}: {variants.Count} prefabs");
            }

            profile.groups = result.ToArray();
            profile.prefabs = Array.Empty<GameObject>();
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            Debug.Log($"Grupos de arbustos de '{profile.name}' refeitos:\n" + string.Join("\n", report));
        }

        /// <summary>Verdadeiro se o perfil ainda usa algum prefab original do Mobile_FV (sem as otimizações de malha e LOD).</summary>
        private static bool UsesLegacyPrefabs(ScatterProfile profile)
        {
            if (profile.prefabs != null)
            {
                foreach (GameObject prefab in profile.prefabs)
                {
                    if (prefab != null && AssetDatabase.GetAssetPath(prefab).StartsWith(LegacyRoot, StringComparison.Ordinal)) return true;
                }
            }

            if (profile.groups != null)
            {
                foreach (ScatterProfile.PrefabGroup group in profile.groups)
                {
                    if (group?.variants == null) continue;

                    foreach (ScatterProfile.GroupVariant variant in group.variants)
                    {
                        if (variant.prefab != null && AssetDatabase.GetAssetPath(variant.prefab).StartsWith(LegacyRoot, StringComparison.Ordinal)) return true;
                    }
                }
            }

            return false;
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

        private static void EnsureShadowLevel(ScatterProfile profile, int level)
        {
            if (profile.castShadowsFromLevel >= 0) return;

            profile.castShadowsFromLevel = level;
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

        private static void MigrateAttempts(ScatterProfile profile, float newValue, params int[] previousDefaults)
        {
            if (Mathf.Approximately(profile.attemptsPerChunk, newValue) || Array.IndexOf(previousDefaults, Mathf.RoundToInt(profile.attemptsPerChunk)) < 0) return;

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
