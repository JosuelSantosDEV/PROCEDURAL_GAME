using UnityEngine;

namespace PROCEDURAL_GAME.World
{
    /// <summary>
    /// Monta a malha (Mesh) de um chunk a partir do <see cref="TerrainNoise"/>.
    /// Reaproveita os mesmos arrays a cada chamada para não gerar lixo (GC), o que importa no celular.
    /// Não é thread-safe: usa um buffer só, então chame de uma vez por vez.
    /// </summary>
    public sealed class ChunkMeshBuilder
    {
        // Metros do mundo por repetição da textura. O UV usa a posição no mundo, então uma textura futura continua sem emendas entre chunks.

        private readonly TerrainNoise terrainNoise;
        private readonly float chunkSize;
        private readonly int resolution;
        private readonly float step;

        // Alturas com 1 vértice de borda em volta do chunk: servem só para calcular normais iguais
        // nos dois lados da divisa entre chunks (senão apareceria uma "costura" na iluminação).
        private readonly int heightGridSize;
        private readonly float[] heights;

        private readonly BiomeProfile biome;
        // A cor de vértice não passa pela conversão sRGB→linear do Unity; no espaço de cor Linear fazemos nós mesmos.
        private readonly bool convertToLinear;

        private readonly Vector3[] vertices;
        private readonly Vector3[] normals;
        // UV0 = pesos das texturas de chão (camadas 0 e 1; o peso da camada 2 é o que sobra até 1). Ver TerrainVertexColor.shader.
        private readonly Vector2[] uvs;
        private readonly Color32[] colors;
        private readonly int[] triangles;

        // Mistura de texturas por zona: só com 2+ camadas de chão e um perfil de objetos que define as zonas.
        private readonly bool blendGround;
        private readonly int seed;
        private readonly int zoneSalt;
        private readonly float zoneSize;
        private readonly float blendMeters;
        private readonly int[] layerOfZone;
        private readonly float[] zoneWeights;

        public ChunkMeshBuilder(TerrainNoise terrainNoise, float chunkSize, int resolution, BiomeProfile biome, int seed)
        {
            this.terrainNoise = terrainNoise;
            this.chunkSize = chunkSize;
            this.biome = biome;
            this.seed = seed;

            ScatterProfile zoneProfile = biome.FindZoneProfile();
            int layerCount = biome.groundLayers != null ? Mathf.Min(biome.groundLayers.Length, BiomeProfile.MaxGroundLayers) : 0;
            if (zoneProfile != null && layerCount >= 2)
            {
                // Zona -> camada de chão pelo id da zona; zona sem camada usa a primeira.
                ScatterProfile.SeasonZone[] zones = zoneProfile.ActiveZones;
                layerOfZone = new int[zones.Length];
                for (int z = 0; z < zones.Length; z++)
                {
                    for (int layer = 0; layer < layerCount; layer++)
                    {
                        if (zones[z] != null && biome.groundLayers[layer].zoneId == zones[z].id)
                        {
                            layerOfZone[z] = layer;
                            break;
                        }
                    }
                }

                zoneWeights = new float[zones.Length];
                zoneSalt = zoneProfile.ZoneSalt;
                zoneSize = zoneProfile.ZoneSize;
                blendMeters = biome.groundBlendMeters;
                blendGround = true;
            }
            convertToLinear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            // O índice de 16 bits do Mesh aceita até 65535 vértices: (254 + 1)² cabe.
            this.resolution = Mathf.Clamp(resolution, 2, 254);
            step = chunkSize / this.resolution;

            int vertsPerSide = this.resolution + 1;
            heightGridSize = this.resolution + 3;

            heights = new float[heightGridSize * heightGridSize];
            vertices = new Vector3[vertsPerSide * vertsPerSide];
            normals = new Vector3[vertices.Length];
            uvs = new Vector2[vertices.Length];
            colors = new Color32[vertices.Length];
            triangles = BuildTriangles(this.resolution);
        }

        /// <summary>Preenche <paramref name="mesh"/> com o relevo do chunk em <paramref name="coord"/> (posição local: o canto do chunk é a origem).</summary>
        public void Build(Mesh mesh, Vector2Int coord)
        {
            float originX = coord.x * chunkSize;
            float originZ = coord.y * chunkSize;

            for (int z = 0; z < heightGridSize; z++)
            {
                for (int x = 0; x < heightGridSize; x++)
                {
                    heights[z * heightGridSize + x] = terrainNoise.GetHeight(originX + (x - 1) * step, originZ + (z - 1) * step);
                }
            }

            int vertsPerSide = resolution + 1;
            for (int z = 0; z < vertsPerSide; z++)
            {
                for (int x = 0; x < vertsPerSide; x++)
                {
                    int vertexIndex = z * vertsPerSide + x;
                    int heightIndex = (z + 1) * heightGridSize + (x + 1);

                    vertices[vertexIndex] = new Vector3(x * step, heights[heightIndex], z * step);

                    // Diferença das alturas vizinhas = inclinação. A normal de uma superfície y = h(x, z)
                    // é proporcional a (-dh/dx, 1, -dh/dz); multiplicamos tudo por 2·step para evitar divisões.
                    float slopeX = heights[heightIndex - 1] - heights[heightIndex + 1];
                    float slopeZ = heights[heightIndex - heightGridSize] - heights[heightIndex + heightGridSize];
                    normals[vertexIndex] = new Vector3(slopeX, 2f * step, slopeZ).normalized;

                    float worldX = originX + x * step;
                    float worldZ = originZ + z * step;

                    uvs[vertexIndex] = GetGroundWeights(worldX, worldZ);
                    colors[vertexIndex] = GetColor(heights[heightIndex], normals[vertexIndex].y, worldX, worldZ);
                }
            }

            mesh.Clear();
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.colors32 = colors;
            mesh.SetTriangles(triangles, 0, false);
            mesh.RecalculateBounds();
        }

        /// <summary>
        /// Cor do chão num ponto: gradiente por altura (vale → chão → alto), rocha nas encostas íngremes
        /// e uma leve variação de brilho para não ficar chapado. O alfa é 1 - rocha (peso da textura de chão no shader).
        /// </summary>
        private Color32 GetColor(float height, float normalY, float worldX, float worldZ)
        {
            float maxHeight = terrainNoise.MaxHeight;
            float heightT = maxHeight > 0f ? Mathf.Clamp01(height / maxHeight) : 0f;

            Color color = Color.Lerp(biome.lowColor, biome.grassColor, Smooth(0f, biome.lowHeightEnd, heightT));
            color = Color.Lerp(color, biome.highColor, Smooth(biome.highHeightStart, 1f, heightT));

            // normalY perto de 1 = chão plano; perto de 0 = parede. Rocha = 1 quando íngreme.
            float rock = 1f - Smooth(biome.rockSlopeFull, biome.rockSlopeStart, normalY);
            color = Color.Lerp(color, biome.rockColor, rock);

            float brightness = 1f + terrainNoise.GetDetail(worldX, worldZ) * biome.colorVariation;

            // O alfa guarda quanto da textura de chão vale neste ponto: 1 no chão, 0 na rocha (a rocha fica só com a cor).
            color = new Color(color.r * brightness, color.g * brightness, color.b * brightness, 1f - rock);

            return convertToLinear ? color.linear : color;
        }

        /// <summary>Peso das camadas de chão 0 e 1 no ponto (a camada 2 recebe o que sobra até 1). Sem mistura: tudo na camada 0.</summary>
        private Vector2 GetGroundWeights(float worldX, float worldZ)
        {
            if (!blendGround) return new Vector2(1f, 0f);

            SeasonZoneMap.Weights(seed, zoneSalt, zoneSize, zoneWeights.Length, blendMeters, worldX, worldZ, zoneWeights);

            float layer0 = 0f;
            float layer1 = 0f;
            for (int z = 0; z < zoneWeights.Length; z++)
            {
                if (layerOfZone[z] == 0) layer0 += zoneWeights[z];
                else if (layerOfZone[z] == 1) layer1 += zoneWeights[z];
            }

            return new Vector2(layer0, layer1);
        }

        // 0 abaixo de 'from', 1 acima de 'to', suave no meio.
        private static float Smooth(float from, float to, float value)
        {
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, value));
        }

        // A grade de triângulos é a mesma para todo chunk, então é montada uma vez só.
        private static int[] BuildTriangles(int resolution)
        {
            int vertsPerSide = resolution + 1;
            int[] result = new int[resolution * resolution * 6];
            int index = 0;

            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    int bottomLeft = z * vertsPerSide + x;
                    int topLeft = bottomLeft + vertsPerSide;
                    int bottomRight = bottomLeft + 1;
                    int topRight = topLeft + 1;

                    // O Unity considera "frente" o sentido horário visto de cima.
                    result[index++] = bottomLeft;
                    result[index++] = topLeft;
                    result[index++] = bottomRight;

                    result[index++] = bottomRight;
                    result[index++] = topLeft;
                    result[index++] = topRight;
                }
            }

            return result;
        }
    }
}
