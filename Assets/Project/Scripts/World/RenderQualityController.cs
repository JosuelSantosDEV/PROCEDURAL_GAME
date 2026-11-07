using PROCEDURAL_GAME.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PROCEDURAL_GAME.World
{
    /// <summary>
    /// Aplica a qualidade de renderização (menu Desempenho > Resolução, reduzida pela qualidade automática): a resolução
    /// interna do jogo é uma fração da tela (Baixo 50%, Performance 60%, Médio 75%, Alto 85%, Ultra 100%) e o HDR fica sempre
    /// desligado. O nível Ultra também liga o pós-processamento (<see cref="UltraPostProcessing"/>); nos outros níveis o passe de
    /// pós da câmera fica desligado, para não custar um quadro cheio extra.
    /// Resolução e HDR vêm do asset do URP em uso (<c>renderScale</c> e <c>supportsHDR</c>, que podem mudar em jogo). A interface
    /// (Canvas) é desenhada depois, na resolução nativa, então textos e botões continuam nítidos.
    /// Nasce sozinho com o <see cref="WorldGenerator"/>; confere o nível todo frame (comparar dois inteiros). Como no
    /// <see cref="ShadowController"/>, o asset é restaurado ao sair para não sujar o arquivo no Editor.
    /// </summary>
    [RequireComponent(typeof(WorldGenerator))]
    // Antes de qualquer Start: o primeiro frame já sai na resolução certa.
    [DefaultExecutionOrder(-95)]
    public class RenderQualityController : MonoBehaviour
    {
        private UniversalRenderPipelineAsset urp;
        private float originalScale;
        private bool originalHdr;
        private int appliedLevel = -1;

        private UniversalAdditionalCameraData cameraData;
        private bool originalPostProcessing;
        private bool postProcessingWanted;
        private Volume volume;
        private VolumeProfile runtimeProfile;

        private void Awake()
        {
            urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

            if (urp == null)
            {
                Debug.LogWarning($"{nameof(RenderQualityController)}: o pipeline em uso não é o URP; a resolução não será ajustada.", this);
                enabled = false;
                return;
            }

            originalScale = urp.renderScale;
            originalHdr = urp.supportsHDR;
            Apply(PerformanceSettings.EffectiveRenderQuality);
            BindCamera();
        }

        private void Update()
        {
            int level = PerformanceSettings.EffectiveRenderQuality;
            if (level != appliedLevel) Apply(level);

            // A câmera pode nascer depois deste componente.
            if (cameraData == null) BindCamera();
        }

        private void OnDestroy()
        {
            // No Editor, mudar o asset do URP em jogo o alteraria de verdade: devolver o que estava.
            if (urp != null)
            {
                urp.renderScale = originalScale;
                urp.supportsHDR = originalHdr;
            }

            if (cameraData != null) cameraData.renderPostProcessing = originalPostProcessing;
            if (runtimeProfile != null) Destroy(runtimeProfile);
        }

        private void Apply(int level)
        {
            appliedLevel = level;
            urp.renderScale = PerformanceSettings.GetRenderScale(level);

            // Sem HDR o render target é mais leve (menos banda de memória); o Ultra faz o pós-processamento em LDR.
            urp.supportsHDR = false;

            postProcessingWanted = PerformanceSettings.UsesPostProcessing(level);
            ApplyPostProcessing();
        }

        private void BindCamera()
        {
            Camera main = Camera.main;
            if (main == null) return;

            cameraData = main.GetUniversalAdditionalCameraData();
            originalPostProcessing = cameraData.renderPostProcessing;
            ApplyPostProcessing();
        }

        private void ApplyPostProcessing()
        {
            if (cameraData != null) cameraData.renderPostProcessing = postProcessingWanted;

            if (postProcessingWanted) EnsureVolume();
            if (volume != null) volume.enabled = postProcessingWanted;
        }

        private void EnsureVolume()
        {
            if (volume != null) return;

            VolumeProfile profile = Resources.Load<VolumeProfile>(UltraPostProcessing.ResourceName);
            if (profile == null)
            {
                Debug.LogWarning($"Perfil '{UltraPostProcessing.ResourceName}' não encontrado em Resources: rode PROCEDURAL_GAME > Quality > Create Ultra Post Processing. " +
                                 "Usando um perfil montado em runtime (no build, pode perder variantes de shader).", this);
                runtimeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
                UltraPostProcessing.Populate(runtimeProfile);
                profile = runtimeProfile;
            }

            GameObject host = new GameObject("UltraPostProcessVolume");
            host.transform.SetParent(transform, false);

            volume = host.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.weight = 1f;
            volume.sharedProfile = profile;
        }
    }
}
