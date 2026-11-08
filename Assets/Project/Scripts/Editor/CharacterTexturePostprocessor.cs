using UnityEditor;
using UnityEngine;

namespace PROCEDURAL_GAME.EditorTools
{
    /// <summary>
    /// Importa as texturas de personagens (Assets/Project/Characters/**) com as opções certas para o celular: cor em sRGB e mapas
    /// de normal (nome terminado em "_Normal") como Normal Map; mipmaps e filtro trilinear; no Android e no iOS reduz para
    /// 1024 px e comprime em ASTC 6x6 (as originais do Fuse têm 2048 px). O arquivo original não é alterado.
    /// Só vale para texturas novas ou reimportadas (clique direito na pasta > Reimport).
    /// </summary>
    public class CharacterTexturePostprocessor : AssetPostprocessor
    {
        private const string Folder = "Assets/Project/Characters/";
        private const string NormalSuffix = "_Normal";
        private const int MobileMaxSize = 1024;
        private static readonly string[] MobilePlatforms = { "Android", "iOS" };

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Folder, System.StringComparison.Ordinal)) return;

            TextureImporter importer = (TextureImporter)assetImporter;

            string fileName = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            bool isNormalMap = fileName.EndsWith(NormalSuffix, System.StringComparison.OrdinalIgnoreCase);

            importer.textureType = isNormalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = !isNormalMap;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 2;
            importer.mipmapEnabled = true;
            importer.isReadable = false;

            foreach (string platform in MobilePlatforms)
            {
                TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(platform);
                settings.overridden = true;
                settings.maxTextureSize = MobileMaxSize;
                settings.format = TextureImporterFormat.ASTC_6x6;
                settings.compressionQuality = 50;
                importer.SetPlatformTextureSettings(settings);
            }
        }
    }
}
