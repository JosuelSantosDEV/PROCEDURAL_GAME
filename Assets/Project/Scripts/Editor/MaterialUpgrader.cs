using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PROCEDURAL_GAME.EditorTools
{
    /// <summary>
    /// Conserta materiais "rosa" de pacotes feitos para outro pipeline (Built-in, HDRP ou shaders CG próprios),
    /// trocando o shader por <c>Universal Render Pipeline/Simple Lit</c> e levando junto textura, cor e recorte (alpha clip).
    /// Simple Lit é mais barato que o Lit, o que importa no celular.
    /// Uso: selecione pastas ou materiais na janela Project e execute o menu; sem seleção, varre todo o Assets.
    /// Materiais que já funcionam no URP (URP/*, Shader Graph, shaders do projeto) são ignorados.
    /// </summary>
    public static class MaterialUpgrader
    {
        private const string TargetShaderName = "Universal Render Pipeline/Simple Lit";

        // Shaders que não devem ser trocados: já são URP ou são de outro tipo (UI, sprite, skybox...).
        private static readonly string[] SkipPrefixes =
        {
            "Universal Render Pipeline/", "Shader Graphs/", "PROCEDURAL_GAME/", "Hidden/Universal",
            "UI/", "Sprites/", "TextMeshPro/", "Skybox/", "GUI/", "Particles/"
        };

        // Nomes de propriedade de cada origem, na ordem de preferência.
        private static readonly string[] ColorMapNames = { "_BaseMap", "_BaseColorMap", "_MainTex" };
        private static readonly string[] ColorNames = { "_BaseColor", "_Color" };
        private static readonly string[] NormalMapNames = { "_BumpMap", "_NormalMap" };

        [MenuItem("PROCEDURAL_GAME/Fix Pink Materials (Convert to URP)")]
        public static void Run()
        {
            Shader target = Shader.Find(TargetShaderName);
            if (target == null)
            {
                Debug.LogError($"Shader '{TargetShaderName}' não encontrado. O URP está instalado e ativo no projeto?");
                return;
            }

            List<Material> materials = CollectMaterials();
            if (materials.Count == 0)
            {
                Debug.Log("Nenhum material incompatível com o URP encontrado.");
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog(
                "Converter materiais para URP",
                $"{materials.Count} material(is) serão alterados no disco (shader trocado por Simple Lit).\n\n" +
                "Se esses arquivos ainda não estão no Git, não há como desfazer. Continuar?",
                "Converter", "Cancelar");
            if (!confirmed) return;

            foreach (Material material in materials) Convert(material, target);

            AssetDatabase.SaveAssets();
            Debug.Log($"{materials.Count} material(is) convertido(s) para {TargetShaderName}. Árvores com vento (shaders Foliage/SpeedTree) ficam estáticas por enquanto.");
        }

        /// <summary>Converte um material se o URP não o desenha (para ferramentas que já sabem quais materiais tratar). Verdadeiro se converteu.</summary>
        public static bool ConvertIfNeeded(Material material)
        {
            Shader target = Shader.Find(TargetShaderName);
            if (target == null || !NeedsConversion(material)) return false;

            Convert(material, target);
            return true;
        }

        private static List<Material> CollectMaterials()
        {
            List<string> folders = new List<string>();
            HashSet<string> guids = new HashSet<string>();

            foreach (Object selected in Selection.GetFiltered<Object>(SelectionMode.Assets))
            {
                string path = AssetDatabase.GetAssetPath(selected);
                if (AssetDatabase.IsValidFolder(path)) folders.Add(path);
                else if (selected is Material) guids.Add(AssetDatabase.AssetPathToGUID(path));
            }

            if (folders.Count == 0 && guids.Count == 0) folders.Add("Assets");
            if (folders.Count > 0) guids.UnionWith(AssetDatabase.FindAssets("t:Material", folders.ToArray()));

            List<Material> result = new List<Material>();
            foreach (string guid in guids)
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (material != null && NeedsConversion(material)) result.Add(material);
            }

            return result;
        }

        private static bool NeedsConversion(Material material)
        {
            string shaderName = material.shader != null ? material.shader.name : string.Empty;

            // Shader ausente (ex.: material de HDRP sem o pacote) cai em "Hidden/InternalErrorShader": precisa converter.
            if (shaderName == "Hidden/InternalErrorShader") return true;

            foreach (string prefix in SkipPrefixes)
            {
                if (shaderName.StartsWith(prefix)) return false;
            }

            return true;
        }

        private static void Convert(Material material, Shader target)
        {
            // Lê antes de trocar o shader: com o shader ausente (HDRP), HasProperty/GetTexture não enxergam nada,
            // então usamos as propriedades salvas no próprio arquivo do material.
            SerializedObject serialized = new SerializedObject(material);
            Texture colorMap = ReadFirstTexture(serialized, ColorMapNames, out Vector2 scale, out Vector2 offset);
            Texture normalMap = ReadFirstTexture(serialized, NormalMapNames, out _, out _);
            Color color = ReadFirstColor(serialized, ColorNames, Color.white);

            // Recorte (folhas, grama). Só HasProperty("_Cutoff") não basta: o SpeedTree embutido do Unity não o expõe
            // e as folhas saíam opacas, com o fundo do atlas branco. Por isso olhamos também a textura: se o alfa
            // dela é marcado como transparência na importação, ela é de recorte. Shader ausente (HDRP) = opaco.
            bool cutout = material.HasProperty("_Cutoff") || TextureUsesAlphaAsTransparency(colorMap);
            float cutoff = material.HasProperty("_Cutoff") ? material.GetFloat("_Cutoff") : 0.5f;

            material.shader = target;

            material.SetTexture("_BaseMap", colorMap);
            material.SetTextureScale("_BaseMap", scale);
            material.SetTextureOffset("_BaseMap", offset);
            material.SetColor("_BaseColor", color);

            material.SetTexture("_BumpMap", normalMap);
            SetKeyword(material, "_NORMALMAP", normalMap != null);

            // Fosco: o brilho especular padrão do Simple Lit deixa pedra e casca de árvore com aspecto de plástico.
            material.SetColor("_SpecColor", new Color(0.1f, 0.1f, 0.1f));

            material.SetFloat("_AlphaClip", cutout ? 1f : 0f);
            material.SetFloat("_Cutoff", cutoff);
            SetKeyword(material, "_ALPHATEST_ON", cutout);
            material.SetOverrideTag("RenderType", cutout ? "TransparentCutout" : "Opaque");
            material.renderQueue = cutout ? (int)RenderQueue.AlphaTest : -1;

            // Folhas são planos finos: precisam ser vistas dos dois lados.
            material.SetFloat("_Cull", cutout ? (float)CullMode.Off : (float)CullMode.Back);

            EditorUtility.SetDirty(material);
        }

        private static bool TextureUsesAlphaAsTransparency(Texture texture)
        {
            if (texture == null) return false;

            TextureImporter importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter;
            return importer != null && importer.alphaIsTransparency && importer.DoesSourceTextureHaveAlpha();
        }

        private static void SetKeyword(Material material, string keyword, bool enabled)
        {
            if (enabled) material.EnableKeyword(keyword);
            else material.DisableKeyword(keyword);
        }

        private static Texture ReadFirstTexture(SerializedObject serialized, string[] names, out Vector2 scale, out Vector2 offset)
        {
            scale = Vector2.one;
            offset = Vector2.zero;

            foreach (string name in names)
            {
                SerializedProperty entry = FindSaved(serialized, "m_TexEnvs", name);
                if (entry == null) continue;

                Texture texture = entry.FindPropertyRelative("m_Texture").objectReferenceValue as Texture;
                if (texture == null) continue;

                scale = entry.FindPropertyRelative("m_Scale").vector2Value;
                offset = entry.FindPropertyRelative("m_Offset").vector2Value;
                return texture;
            }

            return null;
        }

        private static Color ReadFirstColor(SerializedObject serialized, string[] names, Color fallback)
        {
            foreach (string name in names)
            {
                SerializedProperty entry = FindSaved(serialized, "m_Colors", name);
                if (entry != null) return entry.colorValue;
            }

            return fallback;
        }

        private static SerializedProperty FindSaved(SerializedObject serialized, string arrayName, string propertyName)
        {
            SerializedProperty list = serialized.FindProperty("m_SavedProperties." + arrayName);
            if (list == null) return null;

            for (int i = 0; i < list.arraySize; i++)
            {
                SerializedProperty entry = list.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("first").stringValue == propertyName) return entry.FindPropertyRelative("second");
            }

            return null;
        }
    }
}
