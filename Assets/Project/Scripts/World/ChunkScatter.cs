using System.Collections.Generic;
using PROCEDURAL_GAME.Core;
using Unity.Mathematics;
using UnityEngine;
using Random = Unity.Mathematics.Random;

namespace PROCEDURAL_GAME.World
{
    /// <summary>Um objeto a colocar: qual variação do prefab e onde.</summary>
    public struct ScatterPlacement
    {
        /// <summary>Índice em <see cref="ScatterProfile.Variants"/>.</summary>
        public int VariantIndex;
        public Vector3 Position;
        public Quaternion Rotation;
        public float Scale;

        /// <summary>Multiplicador do colisor do grupo da variação (1 = padrão do perfil).</summary>
        public float ColliderScale;
    }

    /// <summary>
    /// Decide onde ficam os objetos de um chunk. É uma função pura de (seed, chunk, perfil): não guarda nada,
    /// então o mesmo chunk sempre recebe os mesmos objetos, mesmo depois de ser descartado e recriado.
    /// Só calcula posições; criar os GameObjects é trabalho do <see cref="ScatterSpawner"/>.
    /// </summary>
    public sealed class ChunkScatter
    {
        // Faixa em que o ruído de agrupamento passa de "nunca" a "sempre" (o ruído fica em torno de -0,9..0,9).
        private const float ClusterMin = -0.3f;
        private const float ClusterMax = 0.5f;

        // Distância usada para medir a inclinação do terreno, em metros.
        private const float SlopeStep = 1f;

        private readonly TerrainNoise terrain;
        private readonly int seed;
        private readonly float chunkSize;
        private readonly float spawnClearSqr;

        // Reaproveitada a cada chamada para não gerar lixo de memória.
        private readonly List<Vector2> accepted = new List<Vector2>();

        // Variações liberadas pela ferramenta de calibração (só usada quando ScatterTuning.HasFilter).
        private readonly List<int> enabledVariants = new List<int>();
        private int[] enabledStart = System.Array.Empty<int>();
        private int[] enabledCount = System.Array.Empty<int>();

        public ChunkScatter(TerrainNoise terrain, int seed, float chunkSize, float spawnClearRadius)
        {
            this.terrain = terrain;
            this.seed = seed;
            this.chunkSize = chunkSize;
            spawnClearSqr = spawnClearRadius * spawnClearRadius;
        }

        public void Generate(Vector2Int coord, ScatterProfile profile, List<ScatterPlacement> results)
        {
            results.Clear();
            accepted.Clear();

            ScatterProfile.Variant[] variants = profile.Variants;
            if (variants.Length == 0) return;

            // Filtro de variações (só com a ferramenta de calibração): tudo desmarcado = nenhum objeto deste perfil.
            bool filtering = ScatterTuning.HasFilter;
            if (filtering)
            {
                enabledVariants.Clear();
                for (int v = 0; v < variants.Length; v++)
                {
                    if (ScatterTuning.IsEnabled(profile, in variants[v])) enabledVariants.Add(v);
                }

                if (enabledVariants.Count == 0) return;

                if (profile.HasGroups)
                {
                    int groupCount = profile.groups.Length;
                    if (enabledStart.Length < groupCount)
                    {
                        enabledStart = new int[groupCount];
                        enabledCount = new int[groupCount];
                    }

                    System.Array.Clear(enabledCount, 0, groupCount);

                    // As variações liberadas seguem a ordem dos grupos, então as de cada grupo ficam seguidas na lista.
                    for (int e = 0; e < enabledVariants.Count; e++)
                    {
                        int g = variants[enabledVariants[e]].group;
                        if (enabledCount[g] == 0) enabledStart[g] = e;
                        enabledCount[g]++;
                    }
                }
            }

            bool grouped = profile.HasGroups;

            Random random = new Random(math.max(1u, math.hash(new int4(seed, coord.x, coord.y, profile.salt))));
            float originX = coord.x * chunkSize;
            float originZ = coord.y * chunkSize;
            float spacingSqr = profile.minSpacing * profile.minSpacing;
            float maxTerrainHeight = Mathf.Max(0.001f, terrain.MaxHeight);
            float minScale = Mathf.Min(profile.minScale, profile.maxScale);
            float maxScale = Mathf.Max(profile.minScale, profile.maxScale);
            float clusterFrequency = 1f / Mathf.Max(1f, profile.clusterSize);

            // Densidade escolhida pelo jogador no menu de Desempenho. Reduzir só corta o fim da sequência de
            // tentativas: os objetos que ficam continuam exatamente nos mesmos lugares.
            int attempts = Mathf.RoundToInt(profile.attemptsPerChunk * PerformanceSettings.GetEffectiveDensity(profile.categoryId));

            for (int i = 0; i < attempts; i++)
            {
                // Sorteia tudo antes de rejeitar: cada tentativa gasta sempre os mesmos números, então mudar
                // uma regra (ex.: inclinação máxima) não embaralha as posições das outras tentativas.
                float x = originX + random.NextFloat() * chunkSize;
                float z = originZ + random.NextFloat() * chunkSize;

                // Um único número sorteado por tentativa, como antes (as posições não mudam). Com grupos, ele escolhe
                // primeiro o TIPO (pelo peso) e depois a variação dentro dele: cada tipo aparece com a mesma frequência,
                // não importa quantas variações tenha. Com filtro, só o mapeamento para o que está liberado muda.
                int variantIndex;
                if (grouped)
                {
                    variantIndex = PickGroupedVariant(profile, random.NextFloat(), filtering);
                }
                else
                {
                    int drawn = random.NextInt(variants.Length);
                    variantIndex = filtering ? enabledVariants[(int)((long)drawn * enabledVariants.Count / variants.Length)] : drawn;
                }

                float yaw = random.NextFloat(360f);
                float scale = random.NextFloat(minScale, maxScale);
                float clusterRoll = random.NextFloat();

                if (x * x + z * z < spawnClearSqr) continue;

                float height = terrain.GetHeight(x, z);
                float normalizedHeight = height / maxTerrainHeight;
                if (normalizedHeight < profile.minHeight || normalizedHeight > profile.maxHeight) continue;

                float slopeX = terrain.GetHeight(x + SlopeStep, z) - terrain.GetHeight(x - SlopeStep, z);
                float slopeZ = terrain.GetHeight(x, z + SlopeStep) - terrain.GetHeight(x, z - SlopeStep);
                Vector3 normal = new Vector3(-slopeX, 2f * SlopeStep, -slopeZ).normalized;
                float slope = Vector3.Angle(Vector3.up, normal);
                if (slope > profile.maxSlope) continue;

                if (profile.clustering > 0f)
                {
                    float cluster = terrain.GetCluster(x, z, clusterFrequency, profile.salt);
                    float chance = Mathf.Lerp(1f, Mathf.InverseLerp(ClusterMin, ClusterMax, cluster), profile.clustering);
                    if (clusterRoll > chance) continue;
                }

                if (IsTooClose(x, z, spacingSqr)) continue;
                accepted.Add(new Vector2(x, z));

                Quaternion tilt = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(Vector3.up, normal), profile.alignToSurface);

                // Altura final = terreno - afundamento fixo do perfil + altura do tipo (metros, absoluto; negativo enterra).
                int group = variants[variantIndex].group;

                results.Add(new ScatterPlacement
                {
                    VariantIndex = variantIndex,
                    Position = new Vector3(x, height - profile.sinkDepth * scale + ScatterTuning.GetElevation(profile, group), z),
                    Rotation = tilt * Quaternion.Euler(0f, yaw, 0f),
                    Scale = scale,
                    ColliderScale = ScatterTuning.GetColliderScale(profile, group)
                });
            }
        }

        /// <summary>
        /// Escolhe o tipo (grupo) com probabilidade proporcional ao peso e, dentro dele, uma variação. Usa um só número
        /// <paramref name="u"/> em [0,1): a parte que sobra depois de achar o tipo escolhe a variação.
        /// </summary>
        private int PickGroupedVariant(ScatterProfile profile, float u, bool filtering)
        {
            int groupCount = profile.groups.Length;

            float total = 0f;
            int lastEligible = -1;
            for (int g = 0; g < groupCount; g++)
            {
                if (VariationsInGroup(profile, g, filtering) == 0) continue;
                total += profile.GetGroupWeight(g);
                lastEligible = g;
            }

            float target = u * total;
            float lower = 0f;
            int chosen = -1;
            float chosenLower = 0f;

            for (int g = 0; g < groupCount; g++)
            {
                if (VariationsInGroup(profile, g, filtering) == 0) continue;

                chosen = g;
                chosenLower = lower;
                lower += profile.GetGroupWeight(g);

                // O último tipo elegível recebe o resto: evita falha por arredondamento de float.
                if (target < lower || g == lastEligible) break;
            }

            int count = VariationsInGroup(profile, chosen, filtering);
            float fraction = Mathf.Clamp01((target - chosenLower) / profile.GetGroupWeight(chosen));
            int offset = Mathf.Min(count - 1, (int)(fraction * count));

            return filtering ? enabledVariants[enabledStart[chosen] + offset] : profile.GetGroupFirst(chosen) + offset;
        }

        private int VariationsInGroup(ScatterProfile profile, int group, bool filtering)
        {
            return filtering ? enabledCount[group] : profile.GetGroupLength(group);
        }

        private bool IsTooClose(float x, float z, float spacingSqr)
        {
            if (spacingSqr <= 0f) return false;

            for (int i = 0; i < accepted.Count; i++)
            {
                float dx = accepted[i].x - x;
                float dz = accepted[i].y - z;
                if (dx * dx + dz * dz < spacingSqr) return true;
            }

            return false;
        }
    }
}
