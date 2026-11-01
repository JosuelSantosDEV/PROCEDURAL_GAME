using System;
using Unity.Mathematics;
using UnityEngine;

namespace PROCEDURAL_GAME.World
{
    /// <summary>Parâmetros do relevo. Ajuste no Inspector do <see cref="WorldGenerator"/>.</summary>
    [Serializable]
    public class NoiseSettings
    {
        [Tooltip("Tamanho das formas do relevo, em metros. Maior = colinas mais largas e suaves.")]
        [Min(1f)] public float scale = 150f;

        [Tooltip("Camadas de ruído somadas. A primeira faz o relevo geral; as seguintes acrescentam detalhe.")]
        [Range(1, 8)] public int octaves = 4;

        [Tooltip("Quanto cada camada de detalhe pesa em relação à anterior (0 a 1).")]
        [Range(0.1f, 0.9f)] public float persistence = 0.45f;

        [Tooltip("Quanto cada camada de detalhe é mais 'fina' que a anterior.")]
        [Min(1f)] public float lacunarity = 2f;

        [Tooltip("Altura máxima do terreno, em metros.")]
        [Min(0f)] public float heightScale = 25f;

        [Tooltip("Molda o relevo: eixo X = ruído (0 a 1), eixo Y = altura (0 a 1). Curva baixa no início = vales planos e picos pontudos.")]
        public AnimationCurve heightCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    }

    /// <summary>
    /// Altura do terreno em qualquer ponto do mundo. É uma função pura de (seed, x, z):
    /// o mesmo ponto sempre dá a mesma altura, então os chunks se encaixam sem emendas
    /// e podem ser descartados e recriados à vontade.
    /// </summary>
    public sealed class TerrainNoise
    {
        // Deslocamentos pequenos: em float, valores grandes perdem precisão e o relevo vira "degraus".
        private const int OffsetRange = 1000;

        private readonly NoiseSettings settings;
        // Variação de cor do chão: formas de ~8 m, bem menores que o relevo.
        private const float DetailFrequency = 0.12f;

        private readonly float2[] octaveOffsets;
        private readonly float2 detailOffset;
        private readonly float2 clusterOffset;
        private readonly float maxAmplitude;

        public TerrainNoise(int seed, NoiseSettings settings)
        {
            this.settings = settings;

            // Cada seed gera um deslocamento diferente por camada, o que muda o relevo inteiro.
            System.Random random = new System.Random(seed);
            int octaves = Mathf.Max(1, settings.octaves);
            octaveOffsets = new float2[octaves];

            float amplitude = 1f;
            for (int i = 0; i < octaves; i++)
            {
                octaveOffsets[i] = new float2(random.Next(-OffsetRange, OffsetRange), random.Next(-OffsetRange, OffsetRange));
                maxAmplitude += amplitude;
                amplitude *= settings.persistence;
            }

            detailOffset = new float2(random.Next(-OffsetRange, OffsetRange), random.Next(-OffsetRange, OffsetRange));

            // Sorteado por último: acrescentar sorteios aqui não muda o relevo nem o detalhe de seeds já conhecidas.
            clusterOffset = new float2(random.Next(-OffsetRange, OffsetRange), random.Next(-OffsetRange, OffsetRange));
        }

        /// <summary>Altura máxima possível do relevo, em metros.</summary>
        public float MaxHeight => settings.heightScale;

        /// <summary>Ruído de detalhe entre -1 e 1, só para variar a cor do chão (não muda a forma do terreno).</summary>
        public float GetDetail(float x, float z)
        {
            return noise.snoise(new float2(x, z) * DetailFrequency + detailOffset);
        }

        /// <summary>
        /// Ruído de baixa frequência (-1 a 1) que decide onde os objetos se agrupam (bosques, pedreiras).
        /// O <paramref name="salt"/> do perfil desloca o ruído, para árvores e pedras formarem manchas diferentes.
        /// </summary>
        public float GetCluster(float x, float z, float frequency, int salt)
        {
            return noise.snoise(new float2(x, z) * frequency + clusterOffset + new float2(salt * 53.7f, salt * 91.3f));
        }

        public float GetHeight(float x, float z)
        {
            float amplitude = 1f;
            float frequency = 1f / settings.scale;
            float total = 0f;

            for (int i = 0; i < octaveOffsets.Length; i++)
            {
                total += noise.snoise(new float2(x * frequency, z * frequency) + octaveOffsets[i]) * amplitude;
                amplitude *= settings.persistence;
                frequency *= settings.lacunarity;
            }

            // snoise devolve algo em torno de -1..1; normalizamos para 0..1 antes da curva.
            float normalized = Mathf.Clamp01(total / maxAmplitude * 0.5f + 0.5f);
            float shaped = settings.heightCurve != null ? settings.heightCurve.Evaluate(normalized) : normalized;
            return shaped * settings.heightScale;
        }
    }
}
