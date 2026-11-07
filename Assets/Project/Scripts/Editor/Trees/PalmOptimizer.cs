using System.Collections.Generic;
using System.IO;
using System.Text;
using PROCEDURAL_GAME.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PROCEDURAL_GAME.EditorTools
{
    /// <summary>
    /// Gera versões leves das palmeiras do pacote "Yughues Free Palm Trees" (menu PROCEDURAL_GAME > Trees > 6 - Build Palms).
    /// Para cada prefab "...OneSided" (folhas de uma face só; o material já desenha os dois lados): junta tronco e folhas
    /// numa malha só com UM material (1 draw call por palmeira, em vez de 2), cria LOD1 e LOD2 (tiram parte das folhas e
    /// ampliam as que ficam, tronco intacto) e um LODGroup com a mesma distância das árvores. O material novo (URP Simple Lit,
    /// recorte, sem mapa de normal) e a textura (1024 px, ASTC no celular) também são ajustados.
    /// Os prefabs saem em Assets/Project/Trees/Palms. Os originais do pacote não são alterados (só a importação da textura).
    /// </summary>
    public static class PalmOptimizer
    {
        private const string SourceFolder = "Assets/Yughues Free Palm Trees/Prefabs";
        private const string DiffusePath = "Assets/Yughues Free Palm Trees/Textures/Palm trees diffuse.tga";
        private const string OutputFolder = "Assets/Project/Trees/Palms";
        private const string MaterialPath = OutputFolder + "/Palm.mat";
        private const string ReportPath = "About/PalmBuild.txt";

        // Mesmas distâncias (m) das árvores do Mobile_FV (TreeOptimizer).
        private const float Lod1Distance = 40f;
        private const float Lod2Distance = 70f;
        private const float CullDistance = 110f;
        private const float ReferenceFov = 60f;

        // Folhas que ficam em cada LOD (sorteio aninhado: o que sobra no LOD2 também está no LOD1) e quanto são ampliadas.
        private const float Lod1Keep = 0.6f;
        private const float Lod1Scale = 1.2f;
        private const float Lod2Keep = 0.3f;
        private const float Lod2Scale = 1.5f;
        private const float MinLodGain = 0.9f;      // um LOD só vale se tiver no máximo esta fração dos triângulos do anterior

        private const float AlphaCutoff = 0.25f;
        private const int TextureMaxSize = 1024;

        private sealed class Geometry
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<bool> IsTrunk = new List<bool>();
            public readonly List<int> Triangles = new List<int>();
        }

        [MenuItem("PROCEDURAL_GAME/Trees/6 - Build Palms")]
        public static void BuildAll()
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { SourceFolder });
            if (guids.Length == 0)
            {
                Debug.LogWarning($"Nenhum prefab de palmeira em '{SourceFolder}'.");
                return;
            }

            EnsureFolder("Assets/Project/Trees", "Palms");
            EnsureFolder(OutputFolder, "Meshes");

            ConfigureTexture();
            Material material = CreateMaterial();
            if (material == null) return;

            StringBuilder report = new StringBuilder();
            int built = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!Path.GetFileNameWithoutExtension(path).EndsWith("OneSided")) continue;

                string line = Build(path, material);
                report.AppendLine(line);
                if (!line.StartsWith("ERRO")) built++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath) ?? "About");
            File.WriteAllText(ReportPath, report.ToString());
            Debug.Log($"Palmeiras: {built} prefab(s) gerado(s) em '{OutputFolder}'. Relatório em {ReportPath}.\n{report}");
        }

        private static string Build(string sourcePath, Material material)
        {
            string name = Path.GetFileNameWithoutExtension(sourcePath).Replace("OneSided", string.Empty);
            string prefabPath = $"{OutputFolder}/{name}.prefab";

            GameObject contents = PrefabUtility.LoadPrefabContents(sourcePath);
            Geometry geometry;
            try
            {
                geometry = Combine(contents);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            if (geometry.Triangles.Count == 0) return $"ERRO {sourcePath}: nenhuma malha legível (a malha do FBX precisa de Read/Write).";

            Mesh lod0 = ToMesh(geometry, 1f, 1f, $"{name}_LOD0");
            int lod0Tris = lod0.triangles.Length / 3;

            Mesh lod1 = ToMesh(geometry, Lod1Keep, Lod1Scale, $"{name}_LOD1");
            int lod1Tris = lod1.triangles.Length / 3;
            if (lod1Tris > lod0Tris * MinLodGain)
            {
                Object.DestroyImmediate(lod1);
                lod1 = null;
                lod1Tris = 0;
            }

            Mesh lod2 = null;
            int lod2Tris = 0;
            if (lod1 != null)
            {
                lod2 = ToMesh(geometry, Lod2Keep, Lod2Scale, $"{name}_LOD2");
                lod2Tris = lod2.triangles.Length / 3;
                if (lod2Tris > lod1Tris * MinLodGain)
                {
                    Object.DestroyImmediate(lod2);
                    lod2 = null;
                    lod2Tris = 0;
                }
            }

            lod0 = SaveMesh(lod0, $"{OutputFolder}/Meshes/{name}_LOD0.asset");
            if (lod1 != null) lod1 = SaveMesh(lod1, $"{OutputFolder}/Meshes/{name}_LOD1.asset");
            if (lod2 != null) lod2 = SaveMesh(lod2, $"{OutputFolder}/Meshes/{name}_LOD2.asset");

            GameObject root = new GameObject(name);
            try
            {
                Renderer r0 = AddMeshChild(root.transform, "LOD0", lod0, material);
                Renderer r1 = lod1 != null ? AddMeshChild(root.transform, "LOD1", lod1, material) : null;
                Renderer r2 = lod2 != null ? AddMeshChild(root.transform, "LOD2", lod2, material) : null;

                LODGroup group = root.AddComponent<LODGroup>();
                group.fadeMode = LODFadeMode.None;
                group.animateCrossFading = false;
                group.SetLODs(BuildLods(r0, r1, r2, 0.5f, 0.25f, 0.1f));
                group.RecalculateBounds();

                float cullHeight = HeightAt(group.size, CullDistance);
                float lod2Height = Mathf.Max(HeightAt(group.size, Lod2Distance), cullHeight + 0.01f);
                float lod1Height = Mathf.Max(HeightAt(group.size, Lod1Distance), (r2 != null ? lod2Height : cullHeight) + 0.01f);
                group.SetLODs(BuildLods(r0, r1, r2, lod1Height, lod2Height, cullHeight));
                group.RecalculateBounds();

                // Centro da base do tronco (o jogo centra o colisor nele, como nas outras árvores).
                Vector3 trunkCenter = FindTrunkCenter(geometry);
                GameObject marker = new GameObject(ScatterInstance.TrunkCenterName);
                marker.transform.SetParent(root.transform, false);
                marker.transform.localPosition = trunkCenter;

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);

                return $"{sourcePath} -> {prefabPath} | {lod0Tris} / {(lod1 != null ? lod1Tris.ToString() : "-")} / {(lod2 != null ? lod2Tris.ToString() : "-")} tri | " +
                       $"altura {group.size:0.0} m | centro do tronco x {trunkCenter.x:0.00} z {trunkCenter.z:0.00}";
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Geometria
        // ---------------------------------------------------------------------------------------------------------------

        /// <summary>Junta todas as malhas do prefab em uma só, no espaço da raiz. Peças com "trunk" no nome são o tronco.</summary>
        private static Geometry Combine(GameObject root)
        {
            Geometry geometry = new Geometry();
            Matrix4x4 toRoot = root.transform.worldToLocalMatrix;

            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null || !mesh.isReadable) continue;

                Matrix4x4 matrix = toRoot * filter.transform.localToWorldMatrix;
                Matrix4x4 normalMatrix = matrix.inverse.transpose;
                bool mirrored = matrix.determinant < 0f;
                bool trunk = filter.gameObject.name.ToLowerInvariant().Contains("trunk");

                Vector3[] vertices = mesh.vertices;
                Vector3[] normals = mesh.normals;
                Vector2[] uvs = mesh.uv;
                int[] triangles = mesh.triangles;
                int offset = geometry.Vertices.Count;

                for (int i = 0; i < vertices.Length; i++)
                {
                    geometry.Vertices.Add(matrix.MultiplyPoint3x4(vertices[i]));
                    geometry.Normals.Add(normals.Length == vertices.Length ? normalMatrix.MultiplyVector(normals[i]).normalized : Vector3.up);
                    geometry.Uvs.Add(uvs.Length == vertices.Length ? uvs[i] : Vector2.zero);
                    geometry.IsTrunk.Add(trunk);
                }

                for (int t = 0; t < triangles.Length; t += 3)
                {
                    geometry.Triangles.Add(offset + triangles[t]);
                    geometry.Triangles.Add(offset + triangles[mirrored ? t + 2 : t + 1]);
                    geometry.Triangles.Add(offset + triangles[mirrored ? t + 1 : t + 2]);
                }
            }

            return geometry;
        }

        /// <summary>
        /// Monta uma malha a partir da geometria. Com <paramref name="keep"/> &lt; 1, mantém só essa fração das folhas (cada folha
        /// é uma peça conectada; sorteio por peça) e amplia as que ficam por <paramref name="scale"/> a partir do ponto em que
        /// encostam no tronco. O tronco nunca é alterado.
        /// </summary>
        private static Mesh ToMesh(Geometry g, float keep, float scale, string meshName)
        {
            int vertexCount = g.Vertices.Count;
            int[] parent = new int[vertexCount];
            for (int i = 0; i < vertexCount; i++) parent[i] = i;

            for (int t = 0; t < g.Triangles.Count; t += 3)
            {
                Union(parent, g.Triangles[t], g.Triangles[t + 1]);
                Union(parent, g.Triangles[t + 1], g.Triangles[t + 2]);
            }

            // Pivô de cada folha = o vértice dela mais perto de qualquer vértice do tronco (onde a folha nasce).
            List<Vector3> trunkVertices = new List<Vector3>();
            for (int v = 0; v < vertexCount; v++)
            {
                if (g.IsTrunk[v]) trunkVertices.Add(g.Vertices[v]);
            }

            Dictionary<int, Vector3> pivot = new Dictionary<int, Vector3>();
            Dictionary<int, float> pivotDistance = new Dictionary<int, float>();

            if (scale != 1f || keep < 1f)
            {
                for (int v = 0; v < vertexCount; v++)
                {
                    if (g.IsTrunk[v]) continue;

                    int root = Find(parent, v);
                    float best = float.MaxValue;
                    foreach (Vector3 trunkVertex in trunkVertices)
                    {
                        best = Mathf.Min(best, (g.Vertices[v] - trunkVertex).sqrMagnitude);
                    }

                    if (!pivotDistance.TryGetValue(root, out float current) || best < current)
                    {
                        pivotDistance[root] = best;
                        pivot[root] = g.Vertices[v];
                    }
                }
            }

            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> triangles = new List<int>();
            int[] remap = new int[vertexCount];
            for (int i = 0; i < vertexCount; i++) remap[i] = -1;

            for (int t = 0; t < g.Triangles.Count; t += 3)
            {
                int a = g.Triangles[t];
                bool trunk = g.IsTrunk[a];
                int root = Find(parent, a);

                if (!trunk && keep < 1f && Hash01(root) >= keep) continue;

                for (int k = 0; k < 3; k++)
                {
                    int v = g.Triangles[t + k];
                    if (remap[v] < 0)
                    {
                        remap[v] = vertices.Count;

                        Vector3 position = g.Vertices[v];
                        if (!trunk && scale != 1f && pivot.TryGetValue(root, out Vector3 origin)) position = origin + (position - origin) * scale;

                        vertices.Add(position);
                        normals.Add(g.Normals[v]);
                        uvs.Add(g.Uvs[v]);
                    }

                    triangles.Add(remap[v]);
                }
            }

            Mesh mesh = new Mesh { name = meshName };
            if (vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Centro (x, z) da base do tronco: caixa dos vértices do tronco na fatia mais baixa (20% da altura).</summary>
        private static Vector3 FindTrunkCenter(Geometry g)
        {
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int v = 0; v < g.Vertices.Count; v++)
            {
                if (!g.IsTrunk[v]) continue;
                minY = Mathf.Min(minY, g.Vertices[v].y);
                maxY = Mathf.Max(maxY, g.Vertices[v].y);
            }

            if (minY > maxY) return Vector3.zero;

            float top = minY + Mathf.Max((maxY - minY) * 0.2f, 0.3f);
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;

            for (int v = 0; v < g.Vertices.Count; v++)
            {
                if (!g.IsTrunk[v] || g.Vertices[v].y > top) continue;
                minX = Mathf.Min(minX, g.Vertices[v].x);
                maxX = Mathf.Max(maxX, g.Vertices[v].x);
                minZ = Mathf.Min(minZ, g.Vertices[v].z);
                maxZ = Mathf.Max(maxZ, g.Vertices[v].z);
            }

            return new Vector3((minX + maxX) * 0.5f, minY, (minZ + maxZ) * 0.5f);
        }

        private static int Find(int[] parent, int index)
        {
            while (parent[index] != index)
            {
                parent[index] = parent[parent[index]];
                index = parent[index];
            }

            return index;
        }

        private static void Union(int[] parent, int a, int b)
        {
            int rootA = Find(parent, a);
            int rootB = Find(parent, b);
            if (rootA != rootB) parent[rootB] = rootA;
        }

        // Número estável de 0 a 1 por peça: o mesmo sorteio nos dois LODs (aninhado: o que fica no LOD2 também está no LOD1).
        private static float Hash01(int value)
        {
            float x = Mathf.Sin(value * 12.9898f + 78.233f) * 43758.5453f;
            return x - Mathf.Floor(x);
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Material, textura e arquivos
        // ---------------------------------------------------------------------------------------------------------------

        private static void ConfigureTexture()
        {
            TextureImporter importer = AssetImporter.GetAtPath(DiffusePath) as TextureImporter;
            if (importer == null) return;

            importer.alphaIsTransparency = true;     // espalha a cor nas bordas do recorte: sem halo claro
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 2;
            // Repeat: as UVs do tronco passam das bordas do atlas (a textura repete ao longo do caule); com Clamp a borda esticava.
            importer.wrapMode = TextureWrapMode.Repeat;

            foreach (string platform in new[] { "Android", "iOS" })
            {
                TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(platform);
                settings.overridden = true;
                settings.maxTextureSize = TextureMaxSize;
                settings.format = TextureImporterFormat.ASTC_6x6;
                settings.compressionQuality = 50;
                importer.SetPlatformTextureSettings(settings);
            }

            importer.SaveAndReimport();
        }

        private static Material CreateMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (shader == null)
            {
                Debug.LogError("Shader 'Universal Render Pipeline/Simple Lit' não encontrado. O URP está instalado e ativo?");
                return null;
            }

            Texture2D diffuse = AssetDatabase.LoadAssetAtPath<Texture2D>(DiffusePath);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, MaterialPath);
            }

            material.shader = shader;
            material.SetTexture("_BaseMap", diffuse);
            material.SetColor("_BaseColor", new Color(0.83f, 0.83f, 0.83f, 1f));
            material.SetColor("_SpecColor", new Color(0.1f, 0.1f, 0.1f, 1f));
            material.SetTexture("_BumpMap", null);
            material.DisableKeyword("_NORMALMAP");
            material.SetFloat("_AlphaClip", 1f);
            material.SetFloat("_Cutoff", AlphaCutoff);
            material.EnableKeyword("_ALPHATEST_ON");
            material.SetOverrideTag("RenderType", "TransparentCutout");
            material.renderQueue = (int)RenderQueue.AlphaTest;
            material.SetFloat("_Cull", (float)CullMode.Off);   // folhas de uma face: vistas dos dois lados

            EditorUtility.SetDirty(material);
            return material;
        }

        private static Mesh SaveMesh(Mesh mesh, string path)
        {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(mesh, existing);
                Object.DestroyImmediate(mesh);
                mesh = existing;
                EditorUtility.SetDirty(mesh);
            }
            else
            {
                AssetDatabase.CreateAsset(mesh, path);
            }

            // O jogo nunca lê vértices em runtime: sem Read/Write o Unity descarta a cópia da CPU.
            SerializedObject serialized = new SerializedObject(mesh);
            SerializedProperty readable = serialized.FindProperty("m_IsReadable");
            if (readable != null)
            {
                readable.boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            return mesh;
        }

        private static Renderer AddMeshChild(Transform parent, string childName, Mesh mesh, Material material)
        {
            GameObject child = new GameObject(childName);
            child.transform.SetParent(parent, false);

            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;   // o jogo decide pelo nível de sombras
            renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return renderer;
        }

        private static LOD[] BuildLods(Renderer lod0, Renderer lod1, Renderer lod2, float lod1Height, float lod2Height, float cullHeight)
        {
            if (lod1 == null) return new[] { new LOD(cullHeight, new[] { lod0 }) };
            if (lod2 == null) return new[] { new LOD(lod1Height, new[] { lod0 }), new LOD(cullHeight, new[] { lod1 }) };
            return new[] { new LOD(lod1Height, new[] { lod0 }), new LOD(lod2Height, new[] { lod1 }), new LOD(cullHeight, new[] { lod2 }) };
        }

        private static float HeightAt(float size, float distance)
        {
            float perMeter = 1f / (2f * Mathf.Tan(ReferenceFov * 0.5f * Mathf.Deg2Rad));
            return Mathf.Clamp(size * perMeter / distance, 0.005f, 0.95f);
        }

        private static void EnsureFolder(string parent, string child)
        {
            if (!AssetDatabase.IsValidFolder($"{parent}/{child}")) AssetDatabase.CreateFolder(parent, child);
        }
    }
}
