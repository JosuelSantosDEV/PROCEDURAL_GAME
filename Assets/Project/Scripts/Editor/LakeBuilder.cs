using System.IO;
using PROCEDURAL_GAME.World;
using UnityEditor;
using UnityEngine;

namespace PROCEDURAL_GAME.EditorTools
{
    /// <summary>
    /// Prepara os lagos da Floresta_01: material da água, texturas de areia (copiadas de Assets/PolyHaven para a pasta de texturas
    /// de chão, onde o importador as configura), os campos <c>lakes</c> do bioma e os perfis de objetos (palmeiras, faixas dos
    /// arbustos e das árvores). Só preenche o que está vazio: ajustes seus nos lagos ficam. É chamado pelo menu
    /// PROCEDURAL_GAME > Create Procedural World e também tem menu próprio.
    /// </summary>
    public static class LakeBuilder
    {
        private const string BiomePath = "Assets/Project/Biomes/Floresta_01.asset";
        private const string MaterialFolder = "Assets/Project/Materials";
        private const string WaterMaterialPath = MaterialFolder + "/Water.mat";
        private const string WaterShaderName = "PROCEDURAL_GAME/Water";
        private const string GroundFolder = "Assets/Project/Textures/Ground";

        // Textura de cor do PolyHaven -> nome na pasta de chão. A areia da margem tem pedrinhas; a do fundo é lisa e escura.
        private const string BeachSource = "Assets/PolyHaven/coast_sand_02_1k.blend/textures/coast_sand_02_diff_1k.jpg";
        private const string BedSource = "Assets/PolyHaven/sand_03_1k.blend/textures/sand_03_diff_1k.jpg";
        private const string BeachTarget = GroundFolder + "/coast_sand_02.jpg";
        private const string BedTarget = GroundFolder + "/sand_03.jpg";

        [MenuItem("PROCEDURAL_GAME/Lakes/Set Up Lakes in Floresta_01")]
        public static void Run()
        {
            BiomeProfile biome = AssetDatabase.LoadAssetAtPath<BiomeProfile>(BiomePath);
            if (biome == null)
            {
                Debug.LogError($"Bioma '{BiomePath}' não encontrado. Rode PROCEDURAL_GAME > Create Procedural World.");
                return;
            }

            EnsureLakes(biome);

            // Mapas de normal do chão (inclui a areia): só pelo menu dos lagos, porque reimporta os biomas.
            GroundNormalMapBuilder.Run();
        }

        /// <summary>Liga e preenche os lagos do bioma (só o que está vazio) e cria os perfis de objetos que dependem deles.</summary>
        public static void EnsureLakes(BiomeProfile biome)
        {
            if (biome == null) return;

            Material water = GetOrCreateWaterMaterial();
            Texture2D beach = ImportSand(BeachSource, BeachTarget);
            Texture2D bed = ImportSand(BedSource, BedTarget);

            if (biome.lakes == null) biome.lakes = new LakeSettings();
            LakeSettings lakes = biome.lakes;

            // Primeira vez (sem material de água): liga os lagos. Depois disso o liga/desliga é seu.
            bool firstTime = lakes.waterMaterial == null;
            if (firstTime && water != null)
            {
                lakes.waterMaterial = water;
                lakes.enabled = true;
            }

            if (lakes.sandTexture == null) lakes.sandTexture = beach;
            if (lakes.bedTexture == null) lakes.bedTexture = bed != null ? bed : beach;

            EditorUtility.SetDirty(biome);
            AssetDatabase.SaveAssets();

            ScatterBuilder.EnsureLakeScatter(biome);

            Debug.Log($"Lagos da '{biome.name}': {(lakes.enabled ? "ligados" : "desligados")}, água {(lakes.waterMaterial != null ? "ok" : "SEM material")}, " +
                      $"areia {(lakes.sandTexture != null ? "ok" : "SEM textura")}, fundo {(lakes.bedTexture != null ? "ok" : "SEM textura")}.");
        }

        private static Material GetOrCreateWaterMaterial()
        {
            Shader shader = Shader.Find(WaterShaderName);
            if (shader == null)
            {
                Debug.LogError($"Shader '{WaterShaderName}' não encontrado (erro de compilação em Assets/Project/Shaders/Water.shader?). Veja o Console.");
                return null;
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(WaterMaterialPath);
            if (material != null)
            {
                if (material.shader != shader)
                {
                    material.shader = shader;
                    EditorUtility.SetDirty(material);
                }

                return material;
            }

            if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/Project", "Materials");

            material = new Material(shader) { name = "Water" };
            AssetDatabase.CreateAsset(material, WaterMaterialPath);
            AssetDatabase.SaveAssets();
            return material;
        }

        // Copia a textura de cor para a pasta de chão (o AssetPostprocessor a importa como Texture2D, repetição, ASTC no celular).
        private static Texture2D ImportSand(string source, string target)
        {
            Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(target);
            if (existing != null) return existing;

            if (!File.Exists(source))
            {
                Debug.LogWarning($"Textura de areia '{source}' não encontrada: lagos ficam sem ela.");
                return null;
            }

            if (!AssetDatabase.CopyAsset(source, target))
            {
                Debug.LogWarning($"Não foi possível copiar '{source}' para '{target}'.");
                return null;
            }

            AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(target);
        }
    }
}
