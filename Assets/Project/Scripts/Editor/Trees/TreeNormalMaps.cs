using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PROCEDURAL_GAME.EditorTools
{
    /// <summary>
    /// Gera mapas de normal (relevo) para a casca das árvores do Mobile_FV, que não tem nenhum: os atlas trazem só a cor.
    /// Para cada atlas usado pelos prefabs de Assets/Project/Trees cria "&lt;atlas&gt;_Normal.png" em Assets/Project/Trees/Textures,
    /// com o MESMO layout (UVs idênticas): plano em todo lugar, menos no bloco onde ficam as faixas de casca, onde o relevo
    /// vem da luminosidade da própria casca. Folhas e arbustos ficam planos (sem efeito); depois liga o mapa nos materiais.
    /// Não altera os atlas originais. Para comparar o custo, o menu 5b desliga tudo de volta.
    /// </summary>
    public static class TreeNormalMaps
    {
        private const string TreesRoot = "Assets/Project/Trees";
        private const string AtlasRoot = "Assets/Mobile_FV/Materials";
        private const string OutputFolder = TreesRoot + "/Textures";

        // Bloco das faixas de casca no atlas de 2048 px, em pixels com origem no canto SUPERIOR esquerdo: x, y, largura, altura.
        // Medido nos atlas de Summer, Spring, Autumn e Winter (o layout é o mesmo nas quatro estações).
        private static readonly RectInt BarkBlock = new RectInt(1088, 720, 2046 - 1088, 2046 - 720);
        private const int ReferenceSize = 2048;

        // Quão fundo é o relevo. Mudar aqui e rodar de novo; o valor pode ser ajustado também por material (Bump Scale).
        private const float Strength = 4f;
        private const float BumpScale = 1f;
        private const int Anisotropy = 4;

        [MenuItem("PROCEDURAL_GAME/Trees/5 - Generate Bark Normal Maps")]
        public static void Generate()
        {
            List<string> atlases = FindAtlases();
            if (atlases.Count == 0)
            {
                Debug.LogWarning($"Nenhum atlas encontrado. Gere as árvores antes (Trees > 3 - Build ALL): os atlas vêm dos prefabs de '{TreesRoot}'.");
                return;
            }

            EnsureFolder(OutputFolder);
            Dictionary<string, Texture2D> normals = new Dictionary<string, Texture2D>();

            try
            {
                for (int i = 0; i < atlases.Count; i++)
                {
                    EditorUtility.DisplayProgressBar("Gerando mapas de normal da casca", atlases[i], i / (float)atlases.Count);
                    Texture2D normal = CreateNormalMap(atlases[i]);
                    if (normal != null) normals[atlases[i]] = normal;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            int materialsChanged = AssignToMaterials(normals);
            AssetDatabase.SaveAssets();

            Debug.Log($"Mapas de normal: {normals.Count} gerado(s), {materialsChanged} material(is) atualizado(s).\n" +
                      string.Join("\n", normals.Select(p => $"  {Path.GetFileName(p.Key)} -> {AssetDatabase.GetAssetPath(p.Value)}")));
        }

        [MenuItem("PROCEDURAL_GAME/Trees/5b - Remove Bark Normal Maps (comparar desempenho)")]
        public static void Remove()
        {
            int changed = 0;
            foreach (Material material in FindAtlasMaterials().Select(m => m.material))
            {
                // Só tira o que esta ferramenta colocou (mapas em Assets/Project/Trees/Textures).
                Texture bump = material.GetTexture("_BumpMap");
                if (bump == null || !AssetDatabase.GetAssetPath(bump).StartsWith(OutputFolder, StringComparison.Ordinal)) continue;

                material.SetTexture("_BumpMap", null);
                material.DisableKeyword("_NORMALMAP");
                EditorUtility.SetDirty(material);
                changed++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"Mapas de normal removidos de {changed} material(is). Os PNGs gerados continuam em '{OutputFolder}'; rode o menu 5 para ligar de novo.");
        }

        // ---------------------------------------------------------------------------------------------------------------

        private static List<string> FindAtlases()
        {
            string[] prefabs = AssetDatabase.FindAssets("t:Prefab", new[] { TreesRoot }).Select(AssetDatabase.GUIDToAssetPath).ToArray();

            return AssetDatabase.GetDependencies(prefabs, true)
                .Where(path => path.StartsWith(AtlasRoot, StringComparison.Ordinal))
                .Where(path => Path.GetFileNameWithoutExtension(path).Contains("Atlas") && !path.Contains("_Snowed"))
                // Os materiais do Winter já usam o próprio mapa de normal (marcas de neve): não mexer neles.
                .Where(path => !path.Contains("/Winter/"))
                .Distinct()
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();
        }

        private readonly struct AtlasMaterial
        {
            public readonly Material material;
            public readonly string atlasPath;

            public AtlasMaterial(Material material, string atlasPath)
            {
                this.material = material;
                this.atlasPath = atlasPath;
            }
        }

        /// <summary>Materiais do Mobile_FV que usam algum atlas como textura base.</summary>
        private static IEnumerable<AtlasMaterial> FindAtlasMaterials()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { AtlasRoot }))
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (material == null || !material.HasProperty("_BaseMap") || !material.HasProperty("_BumpMap")) continue;

                Texture baseMap = material.GetTexture("_BaseMap");
                string path = baseMap != null ? AssetDatabase.GetAssetPath(baseMap) : null;
                if (!string.IsNullOrEmpty(path) && Path.GetFileNameWithoutExtension(path).Contains("Atlas")) yield return new AtlasMaterial(material, path);
            }
        }

        /// <summary>Verdadeiro se o material já tem um mapa de normal que não foi gerado por esta ferramenta.</summary>
        private static bool HasForeignNormalMap(Material material)
        {
            Texture bump = material.GetTexture("_BumpMap");
            return bump != null && !AssetDatabase.GetAssetPath(bump).StartsWith(OutputFolder, StringComparison.Ordinal);
        }

        private static int AssignToMaterials(Dictionary<string, Texture2D> normals)
        {
            int changed = 0;

            foreach (AtlasMaterial item in FindAtlasMaterials())
            {
                if (!normals.TryGetValue(item.atlasPath, out Texture2D normal)) continue;

                // Nunca troca um mapa de normal que o pacote já trazia.
                if (HasForeignNormalMap(item.material)) continue;

                item.material.SetTexture("_BumpMap", normal);
                item.material.SetFloat("_BumpScale", BumpScale);
                item.material.EnableKeyword("_NORMALMAP");
                EditorUtility.SetDirty(item.material);
                changed++;
            }

            return changed;
        }

        private static Texture2D CreateNormalMap(string atlasPath)
        {
            Texture2D source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!source.LoadImage(File.ReadAllBytes(Path.GetFullPath(atlasPath))))
                {
                    Debug.LogError($"Não consegui ler '{atlasPath}'.");
                    return null;
                }

                int width = source.width;
                int height = source.height;
                Color32[] colors = source.GetPixels32(); // linhas de baixo para cima

                // Bloco da casca em pixels desta textura (a origem das linhas aqui é embaixo).
                float scale = width / (float)ReferenceSize;
                int x0 = Mathf.RoundToInt(BarkBlock.x * scale);
                int blockWidth = Mathf.RoundToInt(BarkBlock.width * scale);
                int blockHeight = Mathf.RoundToInt(BarkBlock.height * scale);
                int rowLow = height - Mathf.RoundToInt((BarkBlock.y + BarkBlock.height) * scale);
                if (x0 < 0 || rowLow < 0 || x0 + blockWidth > width || rowLow + blockHeight > height)
                {
                    Debug.LogError($"O bloco da casca não cabe em '{atlasPath}' ({width}x{height}).");
                    return null;
                }

                float[] heights = BlurredLuminance(colors, width, x0, rowLow, blockWidth, blockHeight);

                Color32[] output = new Color32[colors.Length];
                Color32 flat = new Color32(128, 128, 255, 255);
                for (int i = 0; i < output.Length; i++) output[i] = flat;

                for (int ry = 0; ry < blockHeight; ry++)
                {
                    for (int rx = 0; rx < blockWidth; rx++)
                    {
                        // Diferença central da altura em u (colunas) e v (linhas): o relevo "sobe" para onde o valor cresce.
                        float dx = heights[ry * blockWidth + Mathf.Min(rx + 1, blockWidth - 1)] - heights[ry * blockWidth + Mathf.Max(rx - 1, 0)];
                        float dy = heights[Mathf.Min(ry + 1, blockHeight - 1) * blockWidth + rx] - heights[Mathf.Max(ry - 1, 0) * blockWidth + rx];

                        Vector3 normal = new Vector3(-dx * Strength, -dy * Strength, 1f).normalized;
                        output[(rowLow + ry) * width + x0 + rx] = new Color32(
                            (byte)Mathf.RoundToInt((normal.x * 0.5f + 0.5f) * 255f),
                            (byte)Mathf.RoundToInt((normal.y * 0.5f + 0.5f) * 255f),
                            (byte)Mathf.RoundToInt((normal.z * 0.5f + 0.5f) * 255f),
                            255);
                    }
                }

                Texture2D result = new Texture2D(width, height, TextureFormat.RGBA32, false);
                result.SetPixels32(output);
                byte[] png = result.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(result);

                string outputPath = $"{OutputFolder}/{Path.GetFileNameWithoutExtension(atlasPath)}_Normal.png";
                File.WriteAllBytes(Path.GetFullPath(outputPath), png);
                AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceSynchronousImport);

                ConfigureImporter(outputPath);
                return AssetDatabase.LoadAssetAtPath<Texture2D>(outputPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        /// <summary>Luminosidade (0 a 1) do bloco da casca, suavizada com média 3x3 para o relevo não herdar o ruído da imagem.</summary>
        private static float[] BlurredLuminance(Color32[] colors, int width, int x0, int rowLow, int blockWidth, int blockHeight)
        {
            float[] raw = new float[blockWidth * blockHeight];
            for (int ry = 0; ry < blockHeight; ry++)
            {
                for (int rx = 0; rx < blockWidth; rx++)
                {
                    Color32 c = colors[(rowLow + ry) * width + x0 + rx];
                    raw[ry * blockWidth + rx] = (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;
                }
            }

            float[] blurred = new float[raw.Length];
            for (int ry = 0; ry < blockHeight; ry++)
            {
                for (int rx = 0; rx < blockWidth; rx++)
                {
                    float sum = 0f;
                    for (int oy = -1; oy <= 1; oy++)
                    {
                        for (int ox = -1; ox <= 1; ox++)
                        {
                            int sx = Mathf.Clamp(rx + ox, 0, blockWidth - 1);
                            int sy = Mathf.Clamp(ry + oy, 0, blockHeight - 1);
                            sum += raw[sy * blockWidth + sx];
                        }
                    }

                    blurred[ry * blockWidth + rx] = sum / 9f;
                }
            }

            return blurred;
        }

        private static void ConfigureImporter(string path)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;

            importer.textureType = TextureImporterType.NormalMap;
            importer.mipmapEnabled = true;
            importer.mipmapFilter = TextureImporterMipFilter.KaiserFilter;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = Anisotropy;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;

            foreach (string platform in new[] { "Android", "iOS" })
            {
                TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(platform);
                settings.overridden = true;
                settings.format = TextureImporterFormat.ASTC_4x4;
                settings.compressionQuality = 100;
                settings.maxTextureSize = 2048;
                importer.SetPlatformTextureSettings(settings);
            }

            importer.SaveAndReimport();
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
