using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PROCEDURAL_GAME.EditorTools
{
    /// <summary>
    /// Ajusta a importação dos atlas de textura das árvores (Assets/Mobile_FV/Materials/&lt;estação&gt;/*Atlas*) para a melhor
    /// nitidez possível no celular, sem mexer na imagem original:
    /// - filtro Trilinear e anisotropia 4 (o nível de qualidade Balanced usa o valor de cada textura);
    /// - compressão ASTC 4x4 na qualidade máxima em Android e iOS (o padrão do Unity usa qualidade 50);
    /// - mipmaps com filtro Kaiser (mais nítidos) e cobertura preservada: as folhas recortadas por alfa não "afinam" de longe.
    /// Só texturas usadas pelos prefabs de Assets/Project/Trees; as versões com neve (*_Snowed) ficam de fora por enquanto.
    /// Pode rodar várias vezes: só reimporta o que ainda não está assim. A compressão ASTC "máxima" demora (alguns segundos por atlas).
    /// </summary>
    public static class TreeTextureQuality
    {
        private const string TreesRoot = "Assets/Project/Trees";
        private const string AtlasRoot = "Assets/Mobile_FV/Materials";
        private const int Anisotropy = 4;
        private static readonly string[] MobilePlatforms = { "Android", "iOS" };

        [MenuItem("PROCEDURAL_GAME/Trees/4 - Improve Atlas Textures")]
        public static void Run()
        {
            List<string> textures = FindAtlases();
            if (textures.Count == 0)
            {
                Debug.LogWarning($"Nenhum atlas encontrado. Gere as árvores antes (Trees > 3 - Build ALL): os atlas vêm dos prefabs de '{TreesRoot}'.");
                return;
            }

            int changed = 0;
            try
            {
                for (int i = 0; i < textures.Count; i++)
                {
                    EditorUtility.DisplayProgressBar("Melhorando texturas das árvores", textures[i], i / (float)textures.Count);
                    if (Apply(textures[i])) changed++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            Debug.Log($"Texturas das árvores: {changed} reimportada(s), {textures.Count - changed} já estavam ajustadas.\n" +
                      string.Join("\n", textures.Select(t => "  " + t)));
        }

        private static List<string> FindAtlases()
        {
            string[] prefabs = AssetDatabase.FindAssets("t:Prefab", new[] { TreesRoot }).Select(AssetDatabase.GUIDToAssetPath).ToArray();

            return AssetDatabase.GetDependencies(prefabs, true)
                .Where(path => path.StartsWith(AtlasRoot, System.StringComparison.Ordinal))
                .Where(path => Path.GetFileNameWithoutExtension(path).Contains("Atlas") && !path.Contains("_Snowed"))
                .Distinct()
                .OrderBy(path => path, System.StringComparer.Ordinal)
                .ToList();
        }

        private static bool Apply(string path)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null || importer.textureType == TextureImporterType.NormalMap) return false;

            bool dirty = false;

            dirty |= Set(importer.anisoLevel != Anisotropy, () => importer.anisoLevel = Anisotropy);
            dirty |= Set(importer.filterMode != FilterMode.Trilinear, () => importer.filterMode = FilterMode.Trilinear);
            dirty |= Set(!importer.mipmapEnabled, () => importer.mipmapEnabled = true);
            dirty |= Set(importer.mipmapFilter != TextureImporterMipFilter.KaiserFilter, () => importer.mipmapFilter = TextureImporterMipFilter.KaiserFilter);
            dirty |= Set(importer.textureCompression != TextureImporterCompression.CompressedHQ, () => importer.textureCompression = TextureImporterCompression.CompressedHQ);

            // Folhas recortadas por alfa: sem isto, os níveis menores do mipmap perdem área e a copa parece rala de longe.
            if (importer.DoesSourceTextureHaveAlpha())
            {
                dirty |= Set(!importer.mipMapsPreserveCoverage, () => importer.mipMapsPreserveCoverage = true);
                dirty |= Set(!Mathf.Approximately(importer.alphaTestReferenceValue, 0.5f), () => importer.alphaTestReferenceValue = 0.5f);
            }

            foreach (string platform in MobilePlatforms)
            {
                TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(platform);
                bool already = settings.overridden && settings.format == TextureImporterFormat.ASTC_4x4 && settings.compressionQuality == 100;
                if (already) continue;

                settings.overridden = true;
                settings.format = TextureImporterFormat.ASTC_4x4;
                settings.compressionQuality = 100;
                settings.maxTextureSize = Mathf.Max(settings.maxTextureSize, 2048);
                importer.SetPlatformTextureSettings(settings);
                dirty = true;
            }

            if (dirty) importer.SaveAndReimport();
            return dirty;
        }

        private static bool Set(bool needed, System.Action apply)
        {
            if (needed) apply();
            return needed;
        }
    }
}
