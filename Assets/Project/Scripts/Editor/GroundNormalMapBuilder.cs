using System.Collections.Generic;
using System.IO;
using System.Text;
using PROCEDURAL_GAME.World;
using UnityEditor;
using UnityEngine;

namespace PROCEDURAL_GAME.EditorTools
{
    /// <summary>
    /// Gera os mapas de normal (relevo) do chão a partir dos EXR do PolyHaven e os liga aos biomas.
    /// Para cada textura de cor em Assets/Project/Textures/Ground (ex.: forest_leaves_03.jpg), procura em Assets/PolyHaven o
    /// arquivo "..._nor_gl..." correspondente (formato EXR, pesado e HDR), reduz para 512 px, grava como PNG
    /// "forest_leaves_03_nrm.png" (o importador o configura como Normal Map) e preenche o campo "Normal" das camadas de chão dos
    /// biomas cuja textura de cor tem esse nome. No celular o importador comprime em ASTC 6x6.
    /// Só precisa rodar uma vez (ou quando entrar uma textura nova). Também reimporta os biomas, para o Unity reler os campos.
    /// </summary>
    public static class GroundNormalMapBuilder
    {
        private const string GroundFolder = "Assets/Project/Textures/Ground";
        private const string SourceRoot = "Assets/PolyHaven";
        private const int Size = 512;

        [MenuItem("PROCEDURAL_GAME/Ground/Generate Normal Maps")]
        public static void Run()
        {
            string[] albedoGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { GroundFolder });
            List<string> albedoPaths = new List<string>();
            foreach (string guid in albedoGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(GroundTexturePostprocessor.NormalSuffix, System.StringComparison.OrdinalIgnoreCase)) albedoPaths.Add(path);
            }

            if (albedoPaths.Count == 0)
            {
                Debug.LogWarning($"Nenhuma textura de chão em '{GroundFolder}'.");
                return;
            }

            StringBuilder report = new StringBuilder();
            Dictionary<string, Texture2D> generated = new Dictionary<string, Texture2D>();

            foreach (string albedoPath in albedoPaths)
            {
                string name = Path.GetFileNameWithoutExtension(albedoPath);
                string result = Generate(name, out Texture2D normal);
                report.AppendLine($"  {name}: {result}");
                if (normal != null) generated[name] = normal;
            }

            int linked = LinkToBiomes(generated);
            Debug.Log($"Mapas de normal do chão: {generated.Count} pronto(s), {linked} camada(s) de bioma preenchida(s).\n{report}");
        }

        private static string Generate(string name, out Texture2D normalAsset)
        {
            normalAsset = null;
            string outputPath = $"{GroundFolder}/{name}{GroundTexturePostprocessor.NormalSuffix}";

            string sourcePath = FindSource(name);
            if (sourcePath == null) return $"EXR '{name}_nor_gl' não encontrado em {SourceRoot}";

            Texture2D source = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);
            if (source == null) return $"'{sourcePath}' não foi importado como textura 2D";

            RenderTexture target = RenderTexture.GetTemporary(Size, Size, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;
            Color[] pixels;

            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                Texture2D readback = new Texture2D(Size, Size, TextureFormat.RGBAHalf, false, true);
                readback.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                pixels = readback.GetPixels();
                Object.DestroyImmediate(readback);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }

            // O EXR do PolyHaven guarda a normal já codificada de 0 a 1 (a normal plana é azul claro). Se vier com valores negativos,
            // está de -1 a 1 e precisa ser recodificada.
            float min = 0f;
            foreach (Color pixel in pixels) min = Mathf.Min(min, pixel.r, pixel.g);
            bool signed = min < -0.05f;

            Texture2D output = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true);
            Color[] encoded = new Color[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                Color pixel = pixels[i];
                if (signed) pixel = new Color(pixel.r * 0.5f + 0.5f, pixel.g * 0.5f + 0.5f, pixel.b * 0.5f + 0.5f, 1f);
                encoded[i] = new Color(Mathf.Clamp01(pixel.r), Mathf.Clamp01(pixel.g), Mathf.Clamp01(pixel.b), 1f);
            }

            output.SetPixels(encoded);
            output.Apply(false);
            File.WriteAllBytes(outputPath, output.EncodeToPNG());
            Object.DestroyImmediate(output);

            AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceUpdate);
            normalAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(outputPath);
            return normalAsset != null ? $"ok ({Size}x{Size}{(signed ? ", recodificado" : string.Empty)}) <- {sourcePath}" : "gravado, mas não importou como textura 2D";
        }

        private static string FindSource(string name)
        {
            if (!AssetDatabase.IsValidFolder(SourceRoot)) return null;

            foreach (string guid in AssetDatabase.FindAssets($"{name}_nor_gl", new[] { SourceRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".exr", System.StringComparison.OrdinalIgnoreCase)) return path;
            }

            return null;
        }

        // Preenche "normal" nas camadas de chão cuja textura de cor tem o mesmo nome e reimporta os biomas.
        private static int LinkToBiomes(Dictionary<string, Texture2D> normals)
        {
            int linked = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:BiomeProfile"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                // Reimporta antes de ler: um bioma importado antes de o script ganhar os campos novos guarda a versão sem eles.
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                BiomeProfile biome = AssetDatabase.LoadAssetAtPath<BiomeProfile>(path);
                if (biome == null) continue;

                bool changed = false;
                if (biome.groundLayers != null)
                {
                    for (int i = 0; i < biome.groundLayers.Length; i++)
                    {
                        Texture2D albedo = biome.groundLayers[i].albedo;
                        if (albedo == null || !normals.TryGetValue(albedo.name, out Texture2D normal)) continue;
                        if (biome.groundLayers[i].normal == normal) continue;

                        biome.groundLayers[i].normal = normal;
                        changed = true;
                        linked++;
                    }
                }

                if (changed) EditorUtility.SetDirty(biome);
            }

            AssetDatabase.SaveAssets();
            return linked;
        }
    }
}
