using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PROCEDURAL_GAME.EditorTools
{
    /// <summary>
    /// Etapa 0 da otimização das árvores do Mobile_FV: só LÊ os prefabs e escreve um relatório (About/TreeAudit.txt).
    /// Para cada prefab: quantos renderers/materiais/triângulos, se já tem LODGroup, se as malhas são legíveis e de qual
    /// arquivo vêm. Também lista prefabs duplicados (mesmas malhas, materiais e posições). Não altera nenhum asset.
    /// </summary>
    public static class TreeAudit
    {
        private static readonly string[] Folders =
        {
            "Assets/Mobile_FV/Prefabs/Autumn", "Assets/Mobile_FV/Prefabs/Spring", "Assets/Mobile_FV/Prefabs/Summer",
            "Assets/Mobile_FV/Prefabs/Winter", "Assets/Mobile_FV/Prefabs/Misc", "Assets/Mobile_FV/Prefabs/GroundCover",
            "Assets/Mobile_FV/Optimized/Autumn", "Assets/Mobile_FV/Optimized/Spring", "Assets/Mobile_FV/Optimized/Summer",
            "Assets/Mobile_FV/Optimized/Winter"
        };

        private sealed class Info
        {
            public string Folder;
            public string Name;
            public int Renderers;
            public int Skinned;
            public int Materials;
            public int Vertices;
            public int Triangles;
            public int LodLevels;
            public string LodHeights = "";
            public int MeshesTotal;
            public int MeshesReadable;
            public string Signature = "";
            public string Shaders = "";
            public readonly HashSet<string> MeshSources = new HashSet<string>();
        }

        [MenuItem("PROCEDURAL_GAME/Trees/0 - Audit Mobile_FV")]
        public static void Run()
        {
            List<Info> all = new List<Info>();
            Dictionary<string, (int total, int readable)> sources = new Dictionary<string, (int, int)>();

            try
            {
                string[] guids = AssetDatabase.FindAssets("t:Prefab", Folders.Where(AssetDatabase.IsValidFolder).ToArray());
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (i % 20 == 0) EditorUtility.DisplayProgressBar("Auditando árvores", path, i / (float)guids.Length);

                    Info info = Inspect(path, sources);
                    if (info != null) all.Add(info);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            string report = BuildReport(all, sources);
            string file = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "About", "TreeAudit.txt"));
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, report, new UTF8Encoding(false));

            Debug.Log($"Auditoria de árvores concluída: {all.Count} prefabs. Relatório em {file}");
        }

        private static Info Inspect(string path, Dictionary<string, (int total, int readable)> sources)
        {
            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (root == null) return null;

            Info info = new Info { Folder = Path.GetDirectoryName(path).Replace('\\', '/'), Name = Path.GetFileNameWithoutExtension(path) };
            HashSet<Material> materials = new HashSet<Material>();
            HashSet<string> shaders = new HashSet<string>();
            List<string> parts = new List<string>();
            Matrix4x4 toRoot = root.transform.worldToLocalMatrix;

            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = null;
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    mesh = skinned.sharedMesh;
                    info.Skinned++;
                }
                else if (renderer is MeshRenderer)
                {
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    mesh = filter != null ? filter.sharedMesh : null;
                }

                if (mesh == null) continue;
                info.Renderers++;

                int triangles = CountTriangles(mesh);
                info.Triangles += triangles;
                info.Vertices += mesh.vertexCount;

                string source = AssetDatabase.GetAssetPath(mesh);
                info.MeshSources.Add(source);
                info.MeshesTotal++;
                if (mesh.isReadable) info.MeshesReadable++;

                sources.TryGetValue(source, out var counts);
                sources[source] = (counts.total + 1, counts.readable + (mesh.isReadable ? 1 : 0));

                List<string> materialIds = new List<string>();
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null) continue;
                    materials.Add(material);
                    if (material.shader != null) shaders.Add(material.shader.name);
                    materialIds.Add(IdOf(material));
                }

                Matrix4x4 matrix = toRoot * renderer.transform.localToWorldMatrix;
                Vector3 position = matrix.GetColumn(3);
                parts.Add($"{IdOf(mesh)}|{string.Join(",", materialIds)}|{position.x:F3},{position.y:F3},{position.z:F3}|{matrix.lossyScale.x:F3}");
            }

            info.Materials = materials.Count;
            info.Shaders = string.Join(", ", shaders.OrderBy(s => s));

            LODGroup lodGroup = root.GetComponentInChildren<LODGroup>(true);
            if (lodGroup != null)
            {
                LOD[] lods = lodGroup.GetLODs();
                info.LodLevels = lods.Length;
                info.LodHeights = string.Join("/", lods.Select(l => l.screenRelativeTransitionHeight.ToString("0.###")));
            }

            parts.Sort(StringComparer.Ordinal);
            info.Signature = Hash(string.Join(";", parts));
            return info;
        }

        private static int CountTriangles(Mesh mesh)
        {
            int total = 0;
            try
            {
                for (int i = 0; i < mesh.subMeshCount; i++)
                {
                    if (mesh.GetTopology(i) == MeshTopology.Triangles) total += (int)(mesh.GetIndexCount(i) / 3);
                }
            }
            catch (Exception)
            {
                return -1;
            }

            return total;
        }

        private static string IdOf(UnityEngine.Object asset)
        {
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long localId)) return $"{guid}:{localId}";
            return asset.name;
        }

        private static string Hash(string text)
        {
            using (MD5 md5 = MD5.Create())
            {
                return BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").Substring(0, 10);
            }
        }

        private static string BuildReport(List<Info> all, Dictionary<string, (int total, int readable)> sources)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine($"AUDITORIA DE ÁRVORES — {DateTime.Now:yyyy-MM-dd HH:mm}   ({all.Count} prefabs)");
            text.AppendLine();

            text.AppendLine("== RESUMO POR PASTA ==");
            text.AppendLine("pasta | prefabs | renderers (média/máx) | triângulos (média/máx) | com LOD | malhas legíveis");
            foreach (IGrouping<string, Info> group in all.GroupBy(i => i.Folder).OrderBy(g => g.Key))
            {
                int withLod = group.Count(i => i.LodLevels > 0);
                int meshes = group.Sum(i => i.MeshesTotal);
                int readable = group.Sum(i => i.MeshesReadable);
                text.AppendLine($"{group.Key} | {group.Count()} | {group.Average(i => i.Renderers):F1}/{group.Max(i => i.Renderers)} | " +
                                $"{group.Average(i => i.Triangles):F0}/{group.Max(i => i.Triangles)} | {withLod} | {readable}/{meshes}");
            }

            text.AppendLine();
            text.AppendLine("== ARQUIVOS DE MALHA (origem) ==  legíveis/total de usos");
            foreach (KeyValuePair<string, (int total, int readable)> pair in sources.OrderBy(p => p.Key))
            {
                string importer = "";
                if (AssetImporter.GetAtPath(pair.Key) is ModelImporter model) importer = $"  [ModelImporter isReadable={model.isReadable}]";
                text.AppendLine($"{pair.Key}: {pair.Value.readable}/{pair.Value.total}{importer}");
            }

            text.AppendLine();
            text.AppendLine("== DUPLICADOS (mesma malha, materiais e posições, na mesma pasta) ==");
            int duplicateGroups = 0;
            foreach (IGrouping<string, Info> folder in all.GroupBy(i => i.Folder).OrderBy(g => g.Key))
            {
                foreach (IGrouping<string, Info> same in folder.Where(i => i.Renderers > 0).GroupBy(i => i.Signature))
                {
                    if (same.Count() < 2) continue;
                    duplicateGroups++;
                    text.AppendLine($"{folder.Key}: {string.Join("  =  ", same.Select(i => i.Name).OrderBy(n => n))}");
                }
            }

            if (duplicateGroups == 0) text.AppendLine("(nenhum)");

            text.AppendLine();
            text.AppendLine("== 25 PREFABS MAIS PESADOS (triângulos) ==");
            foreach (Info info in all.OrderByDescending(i => i.Triangles).Take(25))
            {
                text.AppendLine($"{info.Triangles,7} tri | {info.Renderers,3} renderers | {info.Materials} mat | LOD {info.LodLevels} | {info.Folder}/{info.Name}");
            }

            text.AppendLine();
            text.AppendLine("== TODOS OS PREFABS ==");
            text.AppendLine("pasta/nome | renderers | materiais | triângulos | vértices | LOD (níveis: alturas) | legíveis | shaders");
            foreach (Info info in all.OrderBy(i => i.Folder).ThenBy(i => i.Name))
            {
                text.AppendLine($"{info.Folder}/{info.Name} | {info.Renderers} | {info.Materials} | {info.Triangles} | {info.Vertices} | " +
                                $"{(info.LodLevels > 0 ? $"{info.LodLevels}: {info.LodHeights}" : "-")} | {info.MeshesReadable}/{info.MeshesTotal} | {info.Shaders}");
            }

            return text.ToString();
        }
    }
}
