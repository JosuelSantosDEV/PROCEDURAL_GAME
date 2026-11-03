using System;
using Unity.Mathematics;
using UnityEngine;

namespace PROCEDURAL_GAME.World
{
    /// <summary>
    /// Mapa das zonas do mundo (estações do ano): células irregulares (Voronoi). O mundo é dividido em quadrados de
    /// <c>size</c> metros, cada um com um ponto-semente deslocado ao acaso e uma zona sorteada. É função só de
    /// seed + posição: o mesmo lugar é sempre da mesma zona. Um lugar só: as árvores (zona "seca", troca brusca) e o chão
    /// (mistura suave de texturas) usam exatamente o mesmo mapa.
    /// </summary>
    public static class SeasonZoneMap
    {
        // Deslocamento do sorteio das zonas para não repetir os outros sorteios do perfil.
        private const int SaltOffset = 104729;

        /// <summary>Zona (0 a zoneCount-1) do ponto: a da semente mais próxima. A troca de uma zona para outra é seca.</summary>
        public static int ZoneAt(int seed, int salt, float size, int zoneCount, float x, float z)
        {
            size = Mathf.Max(20f, size);
            int cellX = Mathf.FloorToInt(x / size);
            int cellZ = Mathf.FloorToInt(z / size);

            float bestDistance = float.MaxValue;
            int bestZone = 0;

            // Sementes ficam entre 15% e 85% da célula: a mais próxima sempre está nas 3x3 células em volta.
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int sx = cellX + dx;
                    int sz = cellZ + dz;
                    SeedPoint(seed, salt, size, sx, sz, out float px, out float pz);

                    float distance = (px - x) * (px - x) + (pz - z) * (pz - z);
                    if (distance >= bestDistance) continue;

                    bestDistance = distance;
                    bestZone = ZoneOfSeed(seed, salt, zoneCount, sx, sz);
                }
            }

            return bestZone;
        }

        /// <summary>
        /// Quanto o ponto pertence a cada zona, de 0 a 1 (a soma é 1). No meio de uma zona vale 1 para ela; perto da fronteira
        /// com outra, os pesos se misturam numa faixa de cerca de <paramref name="blendMeters"/> metros (medida pela diferença
        /// entre a distância à semente mais próxima e a outra), e o peso é 50% / 50% sobre a própria fronteira.
        /// </summary>
        /// <param name="weights">Vetor com pelo menos <paramref name="zoneCount"/> posições, preenchido pela função.</param>
        public static void Weights(int seed, int salt, float size, int zoneCount, float blendMeters, float x, float z, float[] weights)
        {
            size = Mathf.Max(20f, size);
            float width = Mathf.Max(0.01f, blendMeters);
            int cellX = Mathf.FloorToInt(x / size);
            int cellZ = Mathf.FloorToInt(z / size);

            Array.Clear(weights, 0, zoneCount);

            // Distância à semente mais próxima.
            float nearest = float.MaxValue;
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    SeedPoint(seed, salt, size, cellX + dx, cellZ + dz, out float px, out float pz);
                    float distance = (px - x) * (px - x) + (pz - z) * (pz - z);
                    if (distance < nearest) nearest = distance;
                }
            }

            nearest = Mathf.Sqrt(nearest);

            // Cada semente contribui menos quanto mais longe estiver além da mais próxima.
            float total = 0f;
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int sx = cellX + dx;
                    int sz = cellZ + dz;
                    SeedPoint(seed, salt, size, sx, sz, out float px, out float pz);

                    float distance = Mathf.Sqrt((px - x) * (px - x) + (pz - z) * (pz - z));
                    float weight = Mathf.Clamp01(1f - (distance - nearest) / width);
                    if (weight <= 0f) continue;

                    weight = weight * weight * (3f - 2f * weight);
                    weights[ZoneOfSeed(seed, salt, zoneCount, sx, sz)] += weight;
                    total += weight;
                }
            }

            if (total <= 0f)
            {
                weights[0] = 1f;
                return;
            }

            for (int i = 0; i < zoneCount; i++) weights[i] /= total;
        }

        private static void SeedPoint(int seed, int salt, float size, int cellX, int cellZ, out float x, out float z)
        {
            uint hash = math.hash(new int4(seed, cellX, cellZ, salt + SaltOffset));
            x = (cellX + 0.15f + 0.7f * ((hash & 0xFFFFu) / 65536f)) * size;
            z = (cellZ + 0.15f + 0.7f * (((hash >> 16) & 0xFFFFu) / 65536f)) * size;
        }

        private static int ZoneOfSeed(int seed, int salt, int zoneCount, int cellX, int cellZ)
        {
            return (int)(math.hash(new int4(cellZ, cellX, seed, salt + SaltOffset + 1)) % (uint)zoneCount);
        }
    }
}
