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
        private const float UvMetersPerTile = 4f;

        private readonly TerrainNoise terrainNoise;
        private readonly float chunkSize;
        private readonly int resolution;
        private readonly float step;

        // Alturas com 1 vértice de borda em volta do chunk: servem só para calcular normais iguais
        // nos dois lados da divisa entre chunks (senão apareceria uma "costura" na iluminação).
        private readonly int heightGridSize;
        private readonly float[] heights;

        private readonly Vector3[] vertices;
        private readonly Vector3[] normals;
        private readonly Vector2[] uvs;
        private readonly int[] triangles;

        public ChunkMeshBuilder(TerrainNoise terrainNoise, float chunkSize, int resolution)
        {
            this.terrainNoise = terrainNoise;
            this.chunkSize = chunkSize;
            // O índice de 16 bits do Mesh aceita até 65535 vértices: (254 + 1)² cabe.
            this.resolution = Mathf.Clamp(resolution, 2, 254);
            step = chunkSize / this.resolution;

            int vertsPerSide = this.resolution + 1;
            heightGridSize = this.resolution + 3;

            heights = new float[heightGridSize * heightGridSize];
            vertices = new Vector3[vertsPerSide * vertsPerSide];
            normals = new Vector3[vertices.Length];
            uvs = new Vector2[vertices.Length];
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

                    uvs[vertexIndex] = new Vector2((originX + x * step) / UvMetersPerTile, (originZ + z * step) / UvMetersPerTile);
                }
            }

            mesh.Clear();
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.SetTriangles(triangles, 0, false);
            mesh.RecalculateBounds();
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
