using PROCEDURAL_GAME.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PROCEDURAL_GAME.EditorTools
{
    /// <summary>
    /// Cria o perfil de pós-processamento do nível Ultra em Assets/Project/Resources/UltraPostProcess.asset, com a receita de
    /// <see cref="UltraPostProcessing"/>. Fica em Resources (e como asset do projeto) para o build manter as variantes de shader
    /// dos efeitos usados; um perfil montado só em runtime pode perder variantes de Bloom/Tonemapping no celular.
    /// </summary>
    public static class UltraPostProcessBuilder
    {
        private const string ParentFolder = "Assets/Project";
        private const string Folder = ParentFolder + "/Resources";
        private const string AssetPath = Folder + "/" + UltraPostProcessing.ResourceName + ".asset";

        /// <summary>Cria (ou refaz, com a receita atual) o perfil.</summary>
        [MenuItem("PROCEDURAL_GAME/Quality/Create Ultra Post Processing")]
        public static void Build() => Ensure(true);

        /// <summary>Cria o perfil só se ainda não existir (usado pelo Create Procedural World).</summary>
        public static void EnsureAsset() => Ensure(false);

        private static void Ensure(bool rebuild)
        {
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(AssetPath);
            if (profile != null && !rebuild) return;

            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder(ParentFolder, "Resources");

            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, AssetPath);
            }
            else
            {
                // Refazer: tira os efeitos antigos (são sub-assets do perfil) antes de montar de novo.
                foreach (VolumeComponent old in profile.components)
                {
                    if (old != null) Object.DestroyImmediate(old, true);
                }

                profile.components.Clear();
            }

            UltraPostProcessing.Populate(profile);

            foreach (VolumeComponent component in profile.components)
            {
                component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(component, profile);
            }

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            Debug.Log($"Perfil de pós-processamento do Ultra gravado em '{AssetPath}' ({profile.components.Count} efeitos).");
        }
    }
}
