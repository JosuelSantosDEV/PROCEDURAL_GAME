using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PROCEDURAL_GAME.EditorTools
{
    /// <summary>
    /// Etapas 1 e 2 da otimização das árvores do Mobile_FV. Lê cada prefab de Assets/Mobile_FV/Prefabs/&lt;pacote&gt; e grava em
    /// Assets/Project/Trees/&lt;pacote&gt; um prefab NOVO e independente:
    /// - LOD0: todas as malhas da árvore juntas em uma só (um submesh por material), com a mesma geometria e UVs do original.
    ///   Antes eram dezenas de renderers (Curved: 78; coníferas: 450); agora é 1 renderer e, nas árvores, 1 draw call.
    /// - LOD1: versão simplificada gerada automaticamente: a madeira (peças com a textura de casca, mesmo as pequenas) fica igual e os cartões
    ///   de folhas (componentes pequenos) são reduzidos a uma fração, com os que sobram ampliados para manter a cobertura.
    /// - LODGroup por distância (constantes no topo): LOD0 até 40 m, LOD1 até 110 m (arbustos: some a 70 m), depois some.
    /// Prefabs duplicados (mesma malha, materiais e posições) não são gerados. Nenhum original é alterado ou apagado.
    /// </summary>
    public static class TreeOptimizer
    {
        private const string SourceFbx = "Assets/Mobile_FV/SourceAssets/SourceMeshes.fbx";
        private const string SourceRoot = "Assets/Mobile_FV/Prefabs";
        private const string OutputRoot = "Assets/Project/Trees";

        // Pastas de origem processadas ('New Branches' fica de fora de propósito).
        private static readonly string[] Packs = { "Autumn", "Spring", "Summer", "Winter", "Misc", "GroundCover" };

        // ---- LOD por DISTÂNCIA, em metros (o LODGroup guarda a altura do objeto na tela; a conversão é feita por prefab) ----
        // Valem para FOV vertical 60 (o da câmera do jogo) e QualitySettings.lodBias = 1 (nível Balanced, o do Android):
        // lodBias 2 dobra as distâncias, 0,4 as reduz a 40%. Para mudar depois de gerado, rode o menu 3 de novo.
        private const float ReferenceFov = 60f;
        private const float TreeLod1Distance = 40f;         // daqui para a frente a árvore usa a versão simplificada (LOD1)
        private const float TreeLod2Distance = 70f;         // daqui para a frente usa a versão mais simples e rústica (LOD2)
        private const float TreeCullDistance = 110f;        // daqui para a frente some (acima do alcance da neblina da Floresta_01)
        private const float GroundCoverCullDistance = 70f;  // arbustos são muitos e pequenos: somem antes

        // ---- Simplificação dos LODs ----
        // LOD1: tira parte das folhas. LOD2 (distância maior, quase sempre sob neblina): tira quase todas as folhas, amplia as que
        // ficam e também metade dos galhos pequenos; tronco e galhos grandes ficam. Os sorteios são aninhados: o que sobra no LOD2
        // também existe no LOD1, então a troca de nível não "muda" a árvore.
        private const int SmallComponentTriangles = 40;  // componente com até isto = cartão de folha (pode ser removido)
        private const float FoliageKeepFraction = 0.4f;  // fração dos cartões de folha que fica no LOD1
        private const float MaxCardScale = 1.2f;         // teto da ampliação dos cartões que ficam
        private const int MinTrianglesForLod1 = 400;     // árvore menor que isto não ganha LOD1 (só some a distância)
        private const float MaxLod1Ratio = 0.75f;        // LOD1 só vale se tiver no máximo esta fração dos triângulos do LOD0
        private const float Lod2FoliageKeep = 0.15f;     // fração dos cartões de folha que fica no LOD2
        private const float Lod2MaxCardScale = 1.8f;     // teto da ampliação dos cartões no LOD2
        private const float Lod2SmallWoodKeep = 0.5f;    // fração dos galhos pequenos (peças de casca com até 40 tri) que fica no LOD2
        private const float MaxLod2Ratio = 0.65f;        // LOD2 só vale se tiver no máximo esta fração dos triângulos do LOD1

        // ---- Madeira x folhas ----
        // A casca ocupa um bloco fixo dos atlas Autumn/Spring/Summer/Winter (Mobile_FV): x 1088..2046 e y 720..2046 de 2048 px,
        // ou seja, u >= 0,53 e v <= 0,65 (v com origem embaixo), com uma pequena folga. Peça cujos vértices estão nesse bloco é
        // madeira (tronco e galhos), mesmo pequena: galhos são muitas peças soltas de poucos triângulos e NÃO podem ser tratadas
        // como folhas (removidas ou ampliadas), o que entortava os galhos no LOD1.
        private const float BarkUMin = 0.525f;
        private const float BarkVMax = 0.655f;

        // ---- Centro do tronco (para o colisor) ----
        private const float TrunkSliceFraction = 0.2f;   // a base do tronco = esta fração mais baixa da altura da madeira
        private const float MinTrunkSlice = 0.3f;        // fatia mínima, em metros do prefab (árvores muito baixas)

        // ---- Colisor de malha do tronco (tipos de base larga, onde a cápsula não basta) ----
        private static readonly string[] TrunkMeshTypes = { "5Arms", "Jungle1", "Tree_single" };
        private const float TrunkMeshHeightFraction = 0.5f;  // só a madeira da metade de baixo da altura
        private const float TrunkMeshStartCell = 0.03f;      // célula inicial da simplificação = esta fração da altura mantida
        private const int TrunkMeshMaxTriangles = 120;       // a célula cresce até a malha ter no máximo isto

        // Nomes usados no modo de teste (poucas árvores para conferir o resultado antes de converter tudo).
        private static readonly HashSet<string> TestNames = new HashSet<string>
        {
            "Sm1_Curved_v1_00", "Sm1_Tree_single_v1_00", "Sm1_Tree_v1_00", "Conifer1_v1_00", "SpiralTree", "Sm1_fernPlant1"
        };

        // ---------------------------------------------------------------------------------------------------------------
        // Menus
        // ---------------------------------------------------------------------------------------------------------------

        [MenuItem("PROCEDURAL_GAME/Trees/1 - Enable Read-Write on source FBX")]
        public static void EnableReadWrite()
        {
            ModelImporter importer = AssetImporter.GetAtPath(SourceFbx) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError($"Não achei '{SourceFbx}'. Ele só pode ser apagado depois de gerar as árvores novas.");
                return;
            }

            if (importer.isReadable)
            {
                Debug.Log("Read/Write do SourceMeshes.fbx já está ligado.");
                return;
            }

            importer.isReadable = true;
            importer.SaveAndReimport();
            Debug.Log("Read/Write ligado em SourceMeshes.fbx (necessário para ler as malhas no Editor).");
        }

        [MenuItem("PROCEDURAL_GAME/Trees/2 - Build Test (6 trees)")]
        public static void BuildTest() => Build(name => TestNames.Contains(name), "teste");

        [MenuItem("PROCEDURAL_GAME/Trees/3 - Build ALL")]
        public static void BuildAll()
        {
            if (!EditorUtility.DisplayDialog("Gerar todas as árvores otimizadas",
                    "Vai gerar centenas de prefabs e malhas em Assets/Project/Trees. Os originais não são alterados. Continuar?", "Gerar", "Cancelar"))
            {
                return;
            }

            Build(name => true, "completo");
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Pipeline
        // ---------------------------------------------------------------------------------------------------------------

        private sealed class Piece
        {
            public Mesh Mesh;
            public int SubMesh;
            public Material Material;
            public Matrix4x4 Matrix;
        }

        private sealed class MeshData
        {
            public Vector3[] Vertices;
            public Vector3[] Normals;
            public Vector4[] Tangents;
            public Vector2[] Uvs;
            public int[][] Triangles;
        }

        /// <summary>Todas as malhas da árvore em uma lista só de vértices, com uma lista de triângulos por material.</summary>
        private sealed class Geometry
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Vector4> Tangents = new List<Vector4>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<Material> Materials = new List<Material>();
            public readonly List<List<int>> Triangles = new List<List<int>>();
            public bool MissingTangents;
            public int MirroredPieces;

            public int TriangleCount => Triangles.Sum(t => t.Count) / 3;
        }

        private static readonly Dictionary<Mesh, MeshData> MeshCache = new Dictionary<Mesh, MeshData>();

        private static void Build(Func<string, bool> nameFilter, string mode)
        {
            ModelImporter importer = AssetImporter.GetAtPath(SourceFbx) as ModelImporter;
            if (importer != null && !importer.isReadable)
            {
                Debug.LogError("Rode antes o menu PROCEDURAL_GAME > Trees > 1 - Enable Read-Write on source FBX.");
                return;
            }

            StringBuilder report = new StringBuilder();
            report.AppendLine($"GERAÇÃO DE ÁRVORES OTIMIZADAS ({mode}) — {DateTime.Now:yyyy-MM-dd HH:mm}");
            report.AppendLine("origem -> destino | peças -> renderers | triângulos LOD0 / LOD1 | vértices | observações");

            int built = 0, skippedDuplicates = 0, failed = 0;
            MeshCache.Clear();

            try
            {
                List<string> sources = CollectSources(nameFilter);

                // Duplicatas: mesma pasta, mesma assinatura (malhas + materiais + posições).
                Dictionary<string, List<(string path, string signature)>> bySignature = new Dictionary<string, List<(string, string)>>();
                Dictionary<string, List<Piece>> piecesByPath = new Dictionary<string, List<Piece>>();
                Dictionary<string, List<string>> notesByPath = new Dictionary<string, List<string>>();

                for (int i = 0; i < sources.Count; i++)
                {
                    string path = sources[i];
                    if (EditorUtility.DisplayCancelableProgressBar("Lendo árvores", path, i / (float)sources.Count)) return;

                    List<Piece> pieces = new List<Piece>();
                    List<string> notes = new List<string>();
                    notesByPath[path] = notes;

                    GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (root == null || !TryCollect(root, pieces, notes))
                    {
                        failed++;
                        report.AppendLine($"{path} -> (ignorado) | {string.Join("; ", notes)}");
                        continue;
                    }

                    piecesByPath[path] = pieces;
                    string key = Path.GetDirectoryName(path).Replace('\\', '/') + "#" + Signature(pieces);
                    if (!bySignature.TryGetValue(key, out var list)) bySignature[key] = list = new List<(string, string)>();
                    list.Add((path, key));
                }

                HashSet<string> skip = new HashSet<string>();
                foreach (List<(string path, string signature)> group in bySignature.Values.Where(g => g.Count > 1))
                {
                    // Fica o de nome sem "young1" e mais curto (ex.: Tree em vez de young1).
                    List<string> ordered = group.Select(g => g.path)
                        .OrderBy(p => Path.GetFileNameWithoutExtension(p).Contains("young1") ? 1 : 0)
                        .ThenBy(p => Path.GetFileNameWithoutExtension(p).Length)
                        .ThenBy(p => p, StringComparer.Ordinal).ToList();

                    foreach (string duplicate in ordered.Skip(1))
                    {
                        skip.Add(duplicate);
                        skippedDuplicates++;
                        report.AppendLine($"{duplicate} -> (duplicata de {Path.GetFileNameWithoutExtension(ordered[0])}, não gerado)");
                    }
                }

                HashSet<string> usedOutputs = new HashSet<string>();
                List<string> toBuild = piecesByPath.Keys.Where(p => !skip.Contains(p)).OrderBy(p => p, StringComparer.Ordinal).ToList();

                for (int i = 0; i < toBuild.Count; i++)
                {
                    string path = toBuild[i];
                    if (EditorUtility.DisplayCancelableProgressBar("Gerando árvores otimizadas", path, i / (float)toBuild.Count)) break;

                    try
                    {
                        string line = BuildOne(path, piecesByPath[path], usedOutputs);
                        report.AppendLine(line);
                        built++;
                    }
                    catch (Exception exception)
                    {
                        failed++;
                        report.AppendLine($"{path} -> FALHOU: {exception.Message}");
                        Debug.LogException(exception);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                MeshCache.Clear();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            report.AppendLine();
            report.AppendLine($"Gerados: {built} | duplicatas ignoradas: {skippedDuplicates} | falhas/ignorados: {failed}");

            string file = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "About", "TreeBuild.txt"));
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, report.ToString(), new UTF8Encoding(false));

            Debug.Log($"Árvores otimizadas ({mode}): {built} geradas, {skippedDuplicates} duplicatas ignoradas, {failed} falhas. Relatório em {file}");
        }

        private static List<string> CollectSources(Func<string, bool> nameFilter)
        {
            List<string> result = new List<string>();
            string[] folders = Packs.Select(p => $"{SourceRoot}/{p}").Where(AssetDatabase.IsValidFolder).ToArray();

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", folders))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (nameFilter(Path.GetFileNameWithoutExtension(path))) result.Add(path);
            }

            return result;
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Leitura do prefab original
        // ---------------------------------------------------------------------------------------------------------------

        private static bool TryCollect(GameObject root, List<Piece> pieces, List<string> notes)
        {
            Matrix4x4 toRoot = root.transform.worldToLocalMatrix;

            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || !IsActiveInPrefab(renderer.transform, root.transform)) continue;

                if (renderer is SkinnedMeshRenderer)
                {
                    notes.Add("tem SkinnedMeshRenderer (não suportado)");
                    return false;
                }

                if (!(renderer is MeshRenderer)) continue;

                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null) continue;

                if (!mesh.isReadable)
                {
                    notes.Add($"malha '{mesh.name}' não é legível (rode o menu 1)");
                    return false;
                }

                Material[] materials = renderer.sharedMaterials;
                Matrix4x4 matrix = toRoot * renderer.transform.localToWorldMatrix;

                // Com menos materiais que submeshes, o Unity não desenha os submeshes que sobram.
                for (int sub = 0; sub < mesh.subMeshCount && sub < materials.Length; sub++)
                {
                    if (materials[sub] != null) pieces.Add(new Piece { Mesh = mesh, SubMesh = sub, Material = materials[sub], Matrix = matrix });
                }
            }

            if (pieces.Count == 0) notes.Add("nenhuma malha visível");
            return pieces.Count > 0;
        }

        private static bool IsActiveInPrefab(Transform transform, Transform root)
        {
            for (Transform current = transform; current != null; current = current.parent)
            {
                if (!current.gameObject.activeSelf) return false;
                if (current == root) break;
            }

            return true;
        }

        private static string Signature(List<Piece> pieces)
        {
            List<string> parts = new List<string>(pieces.Count);
            foreach (Piece piece in pieces)
            {
                Vector3 position = piece.Matrix.GetColumn(3);
                parts.Add($"{IdOf(piece.Mesh)}#{piece.SubMesh}|{IdOf(piece.Material)}|{position.x:F3},{position.y:F3},{position.z:F3}|{piece.Matrix.lossyScale.x:F3}");
            }

            parts.Sort(StringComparer.Ordinal);
            using (MD5 md5 = MD5.Create())
            {
                return BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(string.Join(";", parts)))).Replace("-", "");
            }
        }

        private static string IdOf(Object asset)
        {
            return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long localId) ? $"{guid}:{localId}" : asset.name;
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Geração de um prefab
        // ---------------------------------------------------------------------------------------------------------------

        private static string BuildOne(string sourcePath, List<Piece> pieces, HashSet<string> usedOutputs)
        {
            string relative = sourcePath.Substring(SourceRoot.Length + 1);
            string folder = $"{OutputRoot}/{Path.GetDirectoryName(relative).Replace('\\', '/')}";
            string baseName = SanitizeName(Path.GetFileNameWithoutExtension(sourcePath));

            string output = $"{folder}/{baseName}.prefab";
            for (int suffix = 2; !usedOutputs.Add(output); suffix++) output = $"{folder}/{baseName}_{suffix}.prefab";
            string outputName = Path.GetFileNameWithoutExtension(output);

            EnsureFolder(folder);
            EnsureFolder($"{folder}/Meshes");

            Geometry geometry = BuildGeometry(pieces);
            int vertices = geometry.Vertices.Count;
            int lod0Triangles = geometry.TriangleCount;

            bool groundCover = relative.StartsWith("GroundCover", StringComparison.Ordinal);
            Mesh lod0Mesh = ToMesh(geometry, outputName + "_LOD0");

            Mesh lod1Mesh = null;
            int lod1Triangles = 0;
            if (!groundCover && lod0Triangles >= MinTrianglesForLod1)
            {
                lod1Mesh = BuildThinnedMesh(geometry, outputName + "_LOD1", FoliageKeepFraction, MaxCardScale, 1f, out lod1Triangles);
                if (lod1Mesh != null && lod1Triangles > lod0Triangles * MaxLod1Ratio)
                {
                    Object.DestroyImmediate(lod1Mesh);
                    lod1Mesh = null;
                }
            }

            // LOD2: só existe se há LOD1 e se for bem mais leve que ele (senão não compensa um nível a mais).
            Mesh lod2Mesh = null;
            int lod2Triangles = 0;
            if (lod1Mesh != null)
            {
                lod2Mesh = BuildThinnedMesh(geometry, outputName + "_LOD2", Lod2FoliageKeep, Lod2MaxCardScale, Lod2SmallWoodKeep, out lod2Triangles);
                if (lod2Mesh != null && lod2Triangles > lod1Triangles * MaxLod2Ratio)
                {
                    Object.DestroyImmediate(lod2Mesh);
                    lod2Mesh = null;
                }
            }

            lod0Mesh = SaveMesh(lod0Mesh, $"{folder}/Meshes/{outputName}_LOD0.asset");
            if (lod1Mesh != null) lod1Mesh = SaveMesh(lod1Mesh, $"{folder}/Meshes/{outputName}_LOD1.asset");
            if (lod2Mesh != null) lod2Mesh = SaveMesh(lod2Mesh, $"{folder}/Meshes/{outputName}_LOD2.asset");

            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            GameObject prefabRoot = new GameObject(outputName);
            Vector3 trunkCenter = Vector3.zero;
            int trunkMeshTriangles = 0;
            try
            {
                prefabRoot.transform.localScale = source.transform.localScale;

                Renderer lod0Renderer = AddMeshChild(prefabRoot.transform, "LOD0", lod0Mesh, geometry.Materials);
                Renderer lod1Renderer = lod1Mesh != null ? AddMeshChild(prefabRoot.transform, "LOD1", lod1Mesh, geometry.Materials) : null;
                Renderer lod2Renderer = lod2Mesh != null ? AddMeshChild(prefabRoot.transform, "LOD2", lod2Mesh, geometry.Materials) : null;

                LODGroup group = prefabRoot.AddComponent<LODGroup>();
                group.fadeMode = LODFadeMode.None;
                group.animateCrossFading = false;

                // O tamanho do grupo só é conhecido depois de calcular os limites; as alturas (tela) saem dele e das distâncias.
                group.SetLODs(BuildLods(lod0Renderer, lod1Renderer, lod2Renderer, 0.5f, 0.25f, 0.1f));
                group.RecalculateBounds();

                float cullDistance = groundCover ? GroundCoverCullDistance : TreeCullDistance;
                float cullHeight = HeightAt(group.size, cullDistance);
                float lod2Height = Mathf.Max(HeightAt(group.size, TreeLod2Distance), cullHeight + 0.01f);
                float lod1Height = Mathf.Max(HeightAt(group.size, TreeLod1Distance), (lod2Renderer != null ? lod2Height : cullHeight) + 0.01f);
                group.SetLODs(BuildLods(lod0Renderer, lod1Renderer, lod2Renderer, lod1Height, lod2Height, cullHeight));
                group.RecalculateBounds();

                // Marcador do centro da base do tronco: o jogo centra o colisor nele (o pivô de alguns modelos não é o tronco).
                if (!groundCover)
                {
                    trunkCenter = FindTrunkCenter(geometry);
                    GameObject marker = new GameObject(PROCEDURAL_GAME.World.ScatterInstance.TrunkCenterName);
                    marker.transform.SetParent(prefabRoot.transform, false);
                    marker.transform.localPosition = trunkCenter;

                    // Colisor de malha do tronco (5Arms, Jungle1, Tree_single): madeira da metade de baixo, simplificada. O jogo o usa no lugar da cápsula.
                    if (WantsTrunkMesh(outputName))
                    {
                        Mesh collisionMesh = BuildTrunkCollisionMesh(geometry);
                        if (collisionMesh != null)
                        {
                            collisionMesh = SaveColliderMesh(collisionMesh, $"{folder}/Meshes/{outputName}_Collider.asset");
                            GameObject colliderObject = new GameObject(PROCEDURAL_GAME.World.ScatterInstance.TrunkColliderName);
                            colliderObject.transform.SetParent(prefabRoot.transform, false);
                            MeshCollider meshCollider = colliderObject.AddComponent<MeshCollider>();
                            meshCollider.sharedMesh = collisionMesh;
                            meshCollider.convex = false;
                            trunkMeshTriangles = collisionMesh.triangles.Length / 3;
                        }
                    }
                }

                PrefabUtility.SaveAsPrefabAsset(prefabRoot, output);
            }
            finally
            {
                Object.DestroyImmediate(prefabRoot);
            }

            List<string> notes = new List<string>();
            if (geometry.MirroredPieces > 0) notes.Add($"{geometry.MirroredPieces} peça(s) espelhada(s) corrigida(s)");
            if (geometry.MissingTangents) notes.Add("tangentes recalculadas");
            if (lod1Mesh == null) notes.Add(groundCover ? "sem LOD1 (vegetação rasteira)" : "sem LOD1");
            else if (lod2Mesh == null) notes.Add("sem LOD2");
            if (!groundCover) notes.Add($"centro do tronco x {trunkCenter.x:0.00} z {trunkCenter.z:0.00}");
            if (trunkMeshTriangles > 0) notes.Add($"colisor de malha do tronco: {trunkMeshTriangles} tri");

            return $"{sourcePath} -> {output} | {pieces.Count} peças -> 1 renderer, {geometry.Materials.Count} material(is) | " +
                   $"{lod0Triangles} / {(lod1Mesh != null ? lod1Triangles.ToString() : "-")} / {(lod2Mesh != null ? lod2Triangles.ToString() : "-")} tri | {vertices} vért | {string.Join("; ", notes)}";
        }

        /// <summary>
        /// Sem LOD1: um nível só (some abaixo de <paramref name="cullHeight"/>). Com LOD1: LOD0 até <paramref name="lod1Height"/>, LOD1 até
        /// <paramref name="cullHeight"/>. Com LOD2: LOD1 só até <paramref name="lod2Height"/> e o LOD2 vai até <paramref name="cullHeight"/>.
        /// </summary>
        private static LOD[] BuildLods(Renderer lod0, Renderer lod1, Renderer lod2, float lod1Height, float lod2Height, float cullHeight)
        {
            if (lod1 == null) return new[] { new LOD(cullHeight, new[] { lod0 }) };
            if (lod2 == null) return new[] { new LOD(lod1Height, new[] { lod0 }), new LOD(cullHeight, new[] { lod1 }) };
            return new[] { new LOD(lod1Height, new[] { lod0 }), new LOD(lod2Height, new[] { lod1 }), new LOD(cullHeight, new[] { lod2 }) };
        }

        /// <summary>Altura relativa na tela (0 a 1) de um objeto de <paramref name="size"/> metros visto a <paramref name="distance"/> metros.</summary>
        private static float HeightAt(float size, float distance)
        {
            float perMeter = 1f / (2f * Mathf.Tan(ReferenceFov * 0.5f * Mathf.Deg2Rad));
            return Mathf.Clamp(size * perMeter / distance, 0.005f, 0.95f);
        }

        private static string SanitizeName(string name)
        {
            // "A1_5Arms_v2_00)" e "A1_Curved_v2_00 " viram nomes limpos; o resto do nome fica igual.
            StringBuilder builder = new StringBuilder();
            foreach (char c in name.Trim())
            {
                if (c == ')' || c == '(') continue;
                builder.Append(c == ' ' ? '_' : c);
            }

            return builder.ToString();
        }

        private static Renderer AddMeshChild(Transform parent, string childName, Mesh mesh, List<Material> materials)
        {
            GameObject child = new GameObject(childName);
            child.transform.SetParent(parent, false);

            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials.ToArray();

            // Projetam e recebem sombra no prefab; no jogo, quem decide é o nível de sombras do menu (ScatterInstance).
            // Sem sondas de luz/reflexo: custo à toa no celular.
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            return renderer;
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Junção das malhas (LOD0)
        // ---------------------------------------------------------------------------------------------------------------

        private static MeshData Read(Mesh mesh)
        {
            if (MeshCache.TryGetValue(mesh, out MeshData data)) return data;

            int count = mesh.vertexCount;
            data = new MeshData
            {
                Vertices = mesh.vertices,
                Normals = mesh.normals,
                Tangents = mesh.tangents,
                Uvs = mesh.uv,
                Triangles = new int[mesh.subMeshCount][]
            };

            if (data.Normals.Length != count) data.Normals = null;
            if (data.Tangents.Length != count) data.Tangents = null;
            if (data.Uvs.Length != count) data.Uvs = null;

            MeshCache[mesh] = data;
            return data;
        }

        private static Geometry BuildGeometry(List<Piece> pieces)
        {
            Geometry geometry = new Geometry();

            foreach (Piece piece in pieces)
            {
                MeshData data = Read(piece.Mesh);
                int[] triangles = data.Triangles[piece.SubMesh] ?? (data.Triangles[piece.SubMesh] = piece.Mesh.GetTriangles(piece.SubMesh));

                int bucket = geometry.Materials.IndexOf(piece.Material);
                if (bucket < 0)
                {
                    geometry.Materials.Add(piece.Material);
                    geometry.Triangles.Add(new List<int>());
                    bucket = geometry.Materials.Count - 1;
                }

                Matrix4x4 matrix = piece.Matrix;
                Matrix4x4 normalMatrix = matrix.inverse.transpose;
                bool mirrored = matrix.determinant < 0f;
                if (mirrored) geometry.MirroredPieces++;

                int baseIndex = geometry.Vertices.Count;
                for (int v = 0; v < data.Vertices.Length; v++)
                {
                    geometry.Vertices.Add(matrix.MultiplyPoint3x4(data.Vertices[v]));
                    geometry.Normals.Add(data.Normals != null ? normalMatrix.MultiplyVector(data.Normals[v]).normalized : Vector3.up);
                    geometry.Uvs.Add(data.Uvs != null ? data.Uvs[v] : Vector2.zero);

                    if (data.Tangents != null)
                    {
                        Vector4 tangent = data.Tangents[v];
                        Vector3 direction = matrix.MultiplyVector(new Vector3(tangent.x, tangent.y, tangent.z)).normalized;
                        geometry.Tangents.Add(new Vector4(direction.x, direction.y, direction.z, mirrored ? -tangent.w : tangent.w));
                    }
                    else
                    {
                        geometry.Tangents.Add(new Vector4(1f, 0f, 0f, 1f));
                        geometry.MissingTangents = true;
                    }
                }

                List<int> target = geometry.Triangles[bucket];
                for (int t = 0; t < triangles.Length; t += 3)
                {
                    // Espelhar (escala negativa) inverte o sentido dos triângulos: sem corrigir, a face some.
                    target.Add(baseIndex + triangles[t]);
                    target.Add(baseIndex + triangles[t + (mirrored ? 2 : 1)]);
                    target.Add(baseIndex + triangles[t + (mirrored ? 1 : 2)]);
                }
            }

            return geometry;
        }

        private static Mesh ToMesh(Geometry geometry, string name)
        {
            Mesh mesh = new Mesh { name = name };
            if (geometry.Vertices.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            mesh.SetVertices(geometry.Vertices);
            mesh.SetNormals(geometry.Normals);
            mesh.SetUVs(0, geometry.Uvs);
            mesh.SetTangents(geometry.Tangents);
            mesh.subMeshCount = geometry.Triangles.Count;
            for (int i = 0; i < geometry.Triangles.Count; i++) mesh.SetTriangles(geometry.Triangles[i], i);

            if (geometry.MissingTangents) mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            MeshUtility.Optimize(mesh);
            return mesh;
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Simplificação (LOD1)
        // ---------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Remove parte dos cartões de folha: agrupa os triângulos em componentes (triângulos que dividem vértices). Os
        /// componentes grandes (tronco, galhos) ficam intactos; os pequenos (cartões) entram numa fração fixa, sorteada
        /// de forma determinística, e os que ficam são ampliados em torno do próprio centro para a copa não ficar rala.
        /// </summary>
        private static Mesh BuildThinnedMesh(Geometry geometry, string name, float foliageKeep, float maxCardScale, float smallWoodKeep, out int triangleCount)
        {
            int vertexCount = geometry.Vertices.Count;
            int[] parent = new int[vertexCount];
            for (int i = 0; i < vertexCount; i++) parent[i] = i;

            foreach (List<int> triangles in geometry.Triangles)
            {
                for (int t = 0; t < triangles.Count; t += 3)
                {
                    Union(parent, triangles[t], triangles[t + 1]);
                    Union(parent, triangles[t + 1], triangles[t + 2]);
                }
            }

            int[] componentTriangles = new int[vertexCount];
            Vector3[] centerSum = new Vector3[vertexCount];
            int[] componentVertices = new int[vertexCount];
            int[] barkVertices = new int[vertexCount];

            foreach (List<int> triangles in geometry.Triangles)
            {
                for (int t = 0; t < triangles.Count; t += 3) componentTriangles[Find(parent, triangles[t])]++;
            }

            for (int v = 0; v < vertexCount; v++)
            {
                int root = Find(parent, v);
                centerSum[root] += geometry.Vertices[v];
                componentVertices[root]++;
                if (IsBarkUv(geometry.Uvs[v])) barkVertices[root]++;
            }

            float scale = Mathf.Min(1f / Mathf.Sqrt(foliageKeep), maxCardScale);

            Mesh mesh = new Mesh { name = name };
            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector4> tangents = new List<Vector4>();
            List<Vector2> uvs = new List<Vector2>();
            List<List<int>> newTriangles = new List<List<int>>();
            int[] remap = new int[vertexCount];
            triangleCount = 0;

            foreach (List<int> triangles in geometry.Triangles)
            {
                List<int> kept = new List<int>();
                for (int i = 0; i < remap.Length; i++) remap[i] = -1;

                for (int t = 0; t < triangles.Count; t += 3)
                {
                    int root = Find(parent, triangles[t]);
                    // Madeira (casca) fica intacta, qualquer que seja o tamanho da peça; só cartões de folha são afinados.
                    bool wood = barkVertices[root] * 2 >= componentVertices[root];
                    bool foliage = !wood && componentTriangles[root] <= SmallComponentTriangles;
                    if (foliage && Hash01(root) >= foliageKeep) continue;

                    // Galhos pequenos só saem nos LODs mais distantes (smallWoodKeep < 1); o tronco e os galhos grandes ficam sempre.
                    if (wood && smallWoodKeep < 1f && componentTriangles[root] <= SmallComponentTriangles && Hash01(root + 100003) >= smallWoodKeep) continue;

                    Vector3 center = centerSum[root] / Mathf.Max(1, componentVertices[root]);
                    for (int corner = 0; corner < 3; corner++)
                    {
                        int original = triangles[t + corner];
                        if (remap[original] < 0)
                        {
                            Vector3 position = geometry.Vertices[original];
                            if (foliage) position = center + (position - center) * scale;

                            remap[original] = vertices.Count;
                            vertices.Add(position);
                            normals.Add(geometry.Normals[original]);
                            tangents.Add(geometry.Tangents[original]);
                            uvs.Add(geometry.Uvs[original]);
                        }

                        kept.Add(remap[original]);
                    }
                }

                newTriangles.Add(kept);
                triangleCount += kept.Count / 3;
            }

            if (triangleCount == 0)
            {
                Object.DestroyImmediate(mesh);
                return null;
            }

            if (vertices.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTangents(tangents);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = newTriangles.Count;
            for (int i = 0; i < newTriangles.Count; i++) mesh.SetTriangles(newTriangles[i], i);

            mesh.RecalculateBounds();
            MeshUtility.Optimize(mesh);
            return mesh;
        }

        /// <summary>
        /// Centro (x, z) da base do tronco, no espaço do prefab; y é o ponto mais baixo da madeira. Madeira = componentes
        /// grandes da malha (os cartões de folha, pequenos, ficam de fora); base = a fatia mais baixa dela. O centro é o da
        /// caixa da fatia, não a média dos vértices, para vários troncos juntos (SkinnyBunches) ou raízes largas (Jungle1)
        /// darem o meio do conjunto em vez de pender para onde há mais triângulos.
        /// </summary>
        private static Vector3 FindTrunkCenter(Geometry geometry)
        {
            int vertexCount = geometry.Vertices.Count;
            if (vertexCount == 0) return Vector3.zero;

            ClassifyWood(geometry, out bool[] isWood, out float minY, out float maxY);

            float sliceTop = minY + Mathf.Max((maxY - minY) * TrunkSliceFraction, MinTrunkSlice);
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;

            for (int v = 0; v < vertexCount; v++)
            {
                Vector3 position = geometry.Vertices[v];
                if (!isWood[v] || position.y > sliceTop) continue;

                minX = Mathf.Min(minX, position.x);
                maxX = Mathf.Max(maxX, position.x);
                minZ = Mathf.Min(minZ, position.z);
                maxZ = Mathf.Max(maxZ, position.z);
            }

            return new Vector3((minX + maxX) * 0.5f, minY, (minZ + maxZ) * 0.5f);
        }

        // Madeira = peça grande ou peça com a textura de casca (os cartões de folha, pequenos e sem casca, ficam de fora).
        // Sem madeira reconhecível, todos os vértices valem. minY/maxY = faixa de altura da madeira.
        private static void ClassifyWood(Geometry geometry, out bool[] isWood, out float minY, out float maxY)
        {
            int vertexCount = geometry.Vertices.Count;
            int[] parent = new int[vertexCount];
            for (int i = 0; i < vertexCount; i++) parent[i] = i;

            foreach (List<int> triangles in geometry.Triangles)
            {
                for (int t = 0; t < triangles.Count; t += 3)
                {
                    Union(parent, triangles[t], triangles[t + 1]);
                    Union(parent, triangles[t + 1], triangles[t + 2]);
                }
            }

            int[] componentTriangles = new int[vertexCount];
            foreach (List<int> triangles in geometry.Triangles)
            {
                for (int t = 0; t < triangles.Count; t += 3) componentTriangles[Find(parent, triangles[t])]++;
            }

            int[] componentVertices = new int[vertexCount];
            int[] barkVertices = new int[vertexCount];
            for (int v = 0; v < vertexCount; v++)
            {
                int root = Find(parent, v);
                componentVertices[root]++;
                if (IsBarkUv(geometry.Uvs[v])) barkVertices[root]++;
            }

            isWood = new bool[vertexCount];
            minY = float.MaxValue;
            maxY = float.MinValue;
            for (int v = 0; v < vertexCount; v++)
            {
                int root = Find(parent, v);
                isWood[v] = componentTriangles[root] > SmallComponentTriangles || barkVertices[root] * 2 >= componentVertices[root];
                if (!isWood[v]) continue;

                minY = Mathf.Min(minY, geometry.Vertices[v].y);
                maxY = Mathf.Max(maxY, geometry.Vertices[v].y);
            }

            if (minY > maxY)
            {
                for (int v = 0; v < vertexCount; v++)
                {
                    isWood[v] = true;
                    minY = Mathf.Min(minY, geometry.Vertices[v].y);
                    maxY = Mathf.Max(maxY, geometry.Vertices[v].y);
                }
            }

        }

        private static bool WantsTrunkMesh(string prefabName)
        {
            foreach (string type in TrunkMeshTypes)
            {
                if (prefabName.Contains(type)) return true;
            }

            return false;
        }

        /// <summary>
        /// Malha de colisão do tronco: só os triângulos de madeira da metade de baixo da árvore (sem folhas nem a copa), simplificados
        /// por agrupamento de vértices numa grade: vértices na mesma célula viram um (a média), triângulos que colapsam ou se
        /// repetem somem. A célula começa pequena e cresce até a malha ter no máximo <see cref="TrunkMeshMaxTriangles"/>.
        /// Fica no espaço do prefab. Devolve null se não houver madeira.
        /// </summary>
        private static Mesh BuildTrunkCollisionMesh(Geometry geometry)
        {
            if (geometry.Vertices.Count == 0) return null;

            ClassifyWood(geometry, out bool[] isWood, out float minY, out float maxY);
            float limit = minY + (maxY - minY) * TrunkMeshHeightFraction;

            // Um triângulo que cruza o limite entra inteiro.
            List<int> kept = new List<int>();
            foreach (List<int> triangles in geometry.Triangles)
            {
                for (int t = 0; t < triangles.Count; t += 3)
                {
                    int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                    if (!isWood[a] || !isWood[b] || !isWood[c]) continue;

                    float lowest = Mathf.Min(geometry.Vertices[a].y, Mathf.Min(geometry.Vertices[b].y, geometry.Vertices[c].y));
                    if (lowest > limit) continue;

                    kept.Add(a);
                    kept.Add(b);
                    kept.Add(c);
                }
            }

            if (kept.Count == 0) return null;

            float cell = Mathf.Max(0.02f, (limit - minY) * TrunkMeshStartCell);
            List<Vector3> vertices = null;
            List<int> indices = null;

            for (int attempt = 0; attempt < 30; attempt++)
            {
                ClusterTriangles(geometry.Vertices, kept, cell, out vertices, out indices);
                if (indices.Count / 3 <= TrunkMeshMaxTriangles) break;
                cell *= 1.25f;
            }

            if (indices.Count == 0) return null;

            Mesh mesh = new Mesh { name = "TrunkCollider" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void ClusterTriangles(List<Vector3> source, List<int> triangles, float cell, out List<Vector3> vertices, out List<int> indices)
        {
            Dictionary<Vector3Int, int> clusterOf = new Dictionary<Vector3Int, int>();
            int[] remap = new int[source.Count];
            for (int i = 0; i < remap.Length; i++) remap[i] = -1;

            List<Vector3> sums = new List<Vector3>();
            List<int> counts = new List<int>();

            foreach (int index in triangles)
            {
                if (remap[index] >= 0) continue;

                Vector3 position = source[index];
                Vector3Int key = new Vector3Int(Mathf.FloorToInt(position.x / cell), Mathf.FloorToInt(position.y / cell), Mathf.FloorToInt(position.z / cell));
                if (!clusterOf.TryGetValue(key, out int cluster))
                {
                    cluster = sums.Count;
                    clusterOf[key] = cluster;
                    sums.Add(Vector3.zero);
                    counts.Add(0);
                }

                sums[cluster] += position;
                counts[cluster]++;
                remap[index] = cluster;
            }

            vertices = new List<Vector3>(sums.Count);
            for (int i = 0; i < sums.Count; i++) vertices.Add(sums[i] / counts[i]);

            indices = new List<int>();
            HashSet<Vector3Int> seen = new HashSet<Vector3Int>();

            for (int t = 0; t < triangles.Count; t += 3)
            {
                int a = remap[triangles[t]], b = remap[triangles[t + 1]], c = remap[triangles[t + 2]];
                if (a == b || b == c || a == c) continue;

                // Mesmo triângulo vindo de peças diferentes: um só.
                int low = Mathf.Min(a, Mathf.Min(b, c));
                int high = Mathf.Max(a, Mathf.Max(b, c));
                int mid = a + b + c - low - high;
                if (!seen.Add(new Vector3Int(low, mid, high))) continue;

                indices.Add(a);
                indices.Add(b);
                indices.Add(c);
            }
        }

        // Sem a regra de "ilegível" do SaveMesh: a malha de colisão é minúscula e o MeshCollider se dá melhor com ela legível.
        private static Mesh SaveColliderMesh(Mesh mesh, string path)
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

            return mesh;
        }

        private static bool IsBarkUv(Vector2 uv) => uv.x >= BarkUMin && uv.y <= BarkVMax;

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

        // Número estável em [0,1) a partir do índice do componente: a mesma árvore sempre perde os mesmos cartões.
        private static float Hash01(int value)
        {
            uint h = (uint)value * 2654435761u;
            h ^= h >> 15;
            h *= 2246822519u;
            h ^= h >> 13;
            return (h & 0xFFFFu) / 65536f;
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Arquivos
        // ---------------------------------------------------------------------------------------------------------------

        private static Mesh SaveMesh(Mesh mesh, string path)
        {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                // Mantém o GUID do asset: prefabs gerados antes continuam apontando para ele.
                EditorUtility.CopySerialized(mesh, existing);
                Object.DestroyImmediate(mesh);
                mesh = existing;
                EditorUtility.SetDirty(mesh);
            }
            else
            {
                AssetDatabase.CreateAsset(mesh, path);
            }

            // O jogo nunca lê vértices em runtime: sem Read/Write o Unity descarta a cópia da CPU (metade da memória da malha).
            SerializedObject serialized = new SerializedObject(mesh);
            SerializedProperty readable = serialized.FindProperty("m_IsReadable");
            if (readable != null)
            {
                readable.boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            return mesh;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
