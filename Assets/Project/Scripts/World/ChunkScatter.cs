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
        public int PrefabIndex;
        public Vector3 Position;
        public Quaternion Rotation;
        public float Scale;
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

            if (profile.prefabs == null || profile.prefabs.Length == 0) return;

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
            int attempts = Mathf.RoundToInt(profile.attemptsPerChunk * PerformanceSettings.GetDensity(profile.category));

            for (int i = 0; i < attempts; i++)
            {
                // Sorteia tudo antes de rejeitar: cada tentativa gasta sempre os mesmos números, então mudar
                // uma regra (ex.: inclinação máxima) não embaralha as posições das outras tentativas.
                float x = originX + random.NextFloat() * chunkSize;
                float z = originZ + random.NextFloat() * chunkSize;
                int prefabIndex = random.NextInt(profile.prefabs.Length);
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
                if (Vector3.Angle(Vector3.up, normal) > profile.maxSlope) continue;

                if (profile.clustering > 0f)
                {
                    float cluster = terrain.GetCluster(x, z, clusterFrequency, profile.salt);
                    float chance = Mathf.Lerp(1f, Mathf.InverseLerp(ClusterMin, ClusterMax, cluster), profile.clustering);
                    if (clusterRoll > chance) continue;
                }

                if (IsTooClose(x, z, spacingSqr)) continue;
                accepted.Add(new Vector2(x, z));

                Quaternion tilt = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(Vector3.up, normal), profile.alignToSurface);

                results.Add(new ScatterPlacement
                {
                    PrefabIndex = prefabIndex,
                    Position = new Vector3(x, height - profile.sinkDepth * scale, z),
                    Rotation = tilt * Quaternion.Euler(0f, yaw, 0f),
                    Scale = scale
                });
            }
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
