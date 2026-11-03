using UnityEditor;
using UnityEngine;

namespace PROCEDURAL_GAME.EditorTools
{
    /// <summary>
    /// Importa as texturas de chão de Assets/Project/Textures/Ground com as opções certas para o jogo, sem precisar mexer em
    /// cada arquivo: cor (sRGB), repetição, mipmaps, filtro trilinear e anisotropia (o chão é visto de lado, quase rasante).
    /// No Android e no iOS a textura é reduzida para 512 px e comprimida em ASTC 6x6 (~5x menos memória que uma de 1024 px sem
    /// compressão); no Editor e no PC fica na resolução original. O arquivo original (1024 px) não é alterado.
    /// Só vale para texturas novas ou reimportadas; para refazer as existentes, clique com o direito na pasta > Reimport.
    /// </summary>
    public class GroundTexturePostprocessor : AssetPostprocessor
    {
        private const string Folder = "Assets/Project/Textures/Ground/";
        public const string NormalSuffix = "_nrm.png";
        private const int MobileMaxSize = 512;
        private const int Anisotropy = 4;
        private static readonly string[] MobilePlatforms = { "Android", "iOS" };

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Folder, System.StringComparison.Ordinal)) return;

            TextureImporter importer = (TextureImporter)assetImporter;

            // Mapas de normal gerados pelo menu PROCEDURAL_GAME > Ground > Generate Normal Maps terminam em "_nrm".
            bool isNormalMap = assetPath.EndsWith(NormalSuffix, System.StringComparison.OrdinalIgnoreCase);
            importer.textureType = isNormalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            // Sempre 2D: um arquivo importado como Cubemap não pode ser atribuído a um campo Texture2D (a referência fica vazia).
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = !isNormalMap;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = Anisotropy;
            importer.mipmapEnabled = true;
            importer.mipmapFilter = TextureImporterMipFilter.KaiserFilter;
            importer.npotScale = TextureImporterNPOTScale.None;
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
