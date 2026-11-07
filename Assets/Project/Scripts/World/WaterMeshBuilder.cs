using System.Collections.Generic;
using UnityEngine;

namespace PROCEDURAL_GAME.World
{
    /// <summary>
    /// Monta a malha de água de um chunk: um plano na altura do lago, só nos quadradinhos que tocam o lago (mais uma folga que fica
    /// escondida sob o terreno da margem). A cor de cada vértice vem da profundidade (raso claro, fundo escuro), então a água é
    /// opaca e não precisa de textura de profundidade. Devolve falso quando o chunk não tem lago (o objeto da água fica desligado).
    /// Reaproveita os buffers a cada chamada. Não é thread-safe.
    /// </summary>
    public sealed class WaterMeshBuilder
    {
        // Lado de cada quadradinho da água, em metros. A margem de baixo serve só para o plano passar da margem por baixo do terreno.
        private const float Step = 2f;
        private const float Margin = 3f;

        private readonly TerrainNoise terrain;
        private readonly LakeMap lakes;
        private readonly LakeSettings settings;
        private readonly float chunkSize;
        private readonly int cells;
        private readonly bool convertToLinear;
        private readonly Color shallow;
        private readonly Color deep;

        private readonly Vector3[] vertices;
        private readonly Color32[] colors;
        private readonly bool[] nearLake;
        private readonly List<int> triangles = new List<int>();

        public WaterMeshBuilder(TerrainNoise terrain, float chunkSize)
        {
            this.terrain = terrain;
            this.chunkSize = chunkSize;
            lakes = terrain.Lakes;
            settings = lakes.Settings;

            cells = Mathf.Max(1, Mathf.RoundToInt(chunkSize / Step));
            int side = cells + 1;
            vertices = new Vector3[side * side];
            colors = new Color32[side * side];
            nearLake = new bool[side * side];

            convertToLinear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            shallow = settings.shallowColor;
            deep = settings.deepColor;
        }

        /// <summary>Preenche <paramref name="mesh"/> (posição local do chunk) e diz se há água neste chunk.</summary>
        public bool Build(Mesh mesh, Vector2Int coord)
        {
            float originX = coord.x * chunkSize;
            float originZ = coord.y * chunkSize;

            // Descarte barato: nenhum lago perto do centro do chunk (a meia diagonal é ~0,71 do lado).
            if (!lakes.Query(originX + chunkSize * 0.5f, originZ + chunkSize * 0.5f, out float centerDistance, out float centerLevel, out _)) return false;
            if (centerDistance - chunkSize * 0.72f > Margin) return false;

            int side = cells + 1;
            float step = chunkSize / cells;
            bool any = false;

            for (int z = 0; z < side; z++)
            {
                for (int x = 0; x < side; x++)
                {
                    int index = z * side + x;
                    float worldX = originX + x * step;
                    float worldZ = originZ + z * step;

                    bool found = lakes.Query(worldX, worldZ, out float d, out float level, out _);
                    if (!found) level = centerLevel;

                    nearLake[index] = found && d < Margin;
                    any |= nearLake[index];

                    vertices[index] = new Vector3(x * step, level, z * step);

                    // Profundidade = nível da água menos o terreno (já escavado). Raso (e a folga sob a margem) = cor clara.
                    float depth = level - terrain.GetHeight(worldX, worldZ);
                    float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(depth / settings.maxDepth));
                    Color color = Color.Lerp(shallow, deep, t);
                    colors[index] = convertToLinear ? color.linear : color;
                }
            }

            if (!any) return false;

            triangles.Clear();
            for (int z = 0; z < cells; z++)
            {
                for (int x = 0; x < cells; x++)
                {
                    int bottomLeft = z * side + x;
                    int topLeft = bottomLeft + side;
                    int bottomRight = bottomLeft + 1;
                    int topRight = topLeft + 1;

                    if (!nearLake[bottomLeft] && !nearLake[topLeft] && !nearLake[bottomRight] && !nearLake[topRight]) continue;

                    // Mesmo sentido do terreno (horário visto de cima).
                    triangles.Add(bottomLeft);
                    triangles.Add(topLeft);
                    triangles.Add(bottomRight);

                    triangles.Add(bottomRight);
                    triangles.Add(topLeft);
                    triangles.Add(topRight);
                }
            }

            mesh.Clear();
            mesh.vertices = vertices;
            mesh.colors32 = colors;
            mesh.SetTriangles(triangles, 0, false);
            mesh.RecalculateBounds();
            return true;
        }
    }
}
