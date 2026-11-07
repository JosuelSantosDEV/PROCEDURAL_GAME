using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PROCEDURAL_GAME.World
{
    /// <summary>
    /// Receita do pós-processamento do nível Ultra (menu Desempenho > Resolução). Um só lugar para os valores: o menu
    /// PROCEDURAL_GAME > Quality > Create Ultra Post Processing grava o perfil em Resources com ela, e o
    /// <see cref="RenderQualityController"/> a usa como reserva se o asset não existir.
    /// Pensada para o clima sombrio do jogo: tom filmico sem estourar cores, um pouco mais de contraste, luzes que brilham
    /// de leve e as bordas da tela escurecidas. O HDR do URP segue desligado, então o bloom trabalha em LDR (só as áreas
    /// muito claras passam do limiar).
    /// </summary>
    public static class UltraPostProcessing
    {
        /// <summary>Nome do perfil em Resources (Assets/Project/Resources/UltraPostProcess.asset).</summary>
        public const string ResourceName = "UltraPostProcess";

        // Ajustes (mexa aqui e rode o menu Create Ultra Post Processing de novo).
        private const float BloomThreshold = 0.85f;
        private const float BloomIntensity = 0.30f;
        private const float BloomScatter = 0.65f;
        private const int BloomMaxIterations = 5;       // passos de desfoque (padrão 6): menos passos = mais barato, brilho mais contido
        private const float PostExposure = 0.1f;        // o Neutral escurece um pouco os tons médios
        private const float Contrast = 12f;
        private const float Saturation = -6f;
        private const float VignetteIntensity = 0.30f;
        private const float VignetteSmoothness = 0.5f;

        /// <summary>Acrescenta ao perfil os efeitos do Ultra (o perfil deve estar vazio).</summary>
        public static void Populate(VolumeProfile profile)
        {
            Tonemapping tonemapping = profile.Add<Tonemapping>(false);
            tonemapping.mode.Override(TonemappingMode.Neutral);

            ColorAdjustments colorAdjustments = profile.Add<ColorAdjustments>(false);
            colorAdjustments.postExposure.Override(PostExposure);
            colorAdjustments.contrast.Override(Contrast);
            colorAdjustments.saturation.Override(Saturation);

            Vignette vignette = profile.Add<Vignette>(false);
            vignette.intensity.Override(VignetteIntensity);
            vignette.smoothness.Override(VignetteSmoothness);

            Bloom bloom = profile.Add<Bloom>(false);
            bloom.threshold.Override(BloomThreshold);
            bloom.intensity.Override(BloomIntensity);
            bloom.scatter.Override(BloomScatter);
            bloom.maxIterations.Override(BloomMaxIterations);
        }
    }
}
