using UnityEngine;

namespace PROCEDURAL_GAME.World
{
    /// <summary>
    /// Contas que ligam a distância de visão (metros) à neblina e ao plano de corte da câmera. Um lugar só: o mundo, a
    /// neblina, a câmera e a qualidade automática usam as mesmas fórmulas.
    /// </summary>
    public static class ViewDistance
    {
        /// <summary>
        /// Neblina exponencial ao quadrado: visível = exp(-(densidade × distância)²). Com densidade × distância = 3, restam
        /// ~0,01% na distância de visão, ou seja, a neblina cobre tudo ali.
        /// </summary>
        public const float FogCoverage = 3f;

        /// <summary>Folga, em metros, do plano de corte além da distância de visão: a câmera não mostra o horizonte vazio.</summary>
        public const float FarClipMargin = 8f;

        /// <summary>De quantos em quantos metros andados o mundo reavalia o que carregar. A carga leva esta folga a mais.</summary>
        public const float RefreshStep = 8f;

        /// <summary>Densidade de neblina que cobre tudo em <paramref name="meters"/> metros.</summary>
        public static float FogDensityFor(float meters) => FogCoverage / Mathf.Max(1f, meters);

        /// <summary>Distância em que uma neblina de <paramref name="density"/> cobre tudo (inverso de <see cref="FogDensityFor"/>).</summary>
        public static float CoverDistance(float density) => FogCoverage / Mathf.Max(0.0001f, density);

        /// <summary>Plano de corte da câmera para uma distância de visão: um pouco além dela.</summary>
        public static float FarClipFor(float meters) => meters + FarClipMargin;
    }
}
