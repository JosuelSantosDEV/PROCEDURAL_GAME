using System;
using UnityEngine;

namespace PROCEDURAL_GAME.World
{
    /// <summary>
    /// Lagos de um bioma (campo <c>lakes</c> do <see cref="BiomeProfile"/>). Os lagos são ESCAVADOS no relevo por <see cref="LakeMap"/>
    /// (um por célula do mundo, com sorteio por seed) e a água é um plano na altura de cada lago, desenhado só onde há lago.
    /// Em volta de cada lago o jogo cria faixas, medidas a partir da margem. De fora para dentro:
    /// floresta normal (árvores e arbustos) → mistura (árvores, arbustos e palmeiras, enquanto o chão vira areia) →
    /// só palmeiras e arbustos (chão já todo de areia) → só areia → água.
    /// </summary>
    [Serializable]
    public class LakeSettings
    {
        [Tooltip("Liga os lagos neste bioma.")]
        public bool enabled = false;

        [Header("Onde e quão grandes")]
        [Tooltip("Lado, em metros, da célula do mundo que pode ter UM lago. Maior = lagos mais raros.")]
        [Min(150f)] public float cellSize = 288f;

        [Tooltip("Chance de uma célula ter lago (0 a 1).")]
        [Range(0f, 1f)] public float chance = 0.6f;

        [Tooltip("Raio médio do lago, em metros (mínimo e máximo sorteados por lago). O contorno é irregular: o raio varia cerca de ±40% conforme o ângulo.")]
        public Vector2 radiusRange = new Vector2(28f, 44f);

        [Tooltip("Profundidade no centro, em metros. Fundo em forma de tigela, com margem suave. 4 a 5 m dá para nadar por baixo.")]
        [Min(0.5f)] public float maxDepth = 4.5f;

        [Tooltip("Nível da água = altura natural do terreno no centro do lago menos isto. Cada lago tem o próprio nível.")]
        public float levelOffset = 0.3f;

        [Tooltip("Sem lago a esta distância (m) do ponto (0, 0): o Player nunca começa na água.")]
        [Min(0f)] public float clearRadius = 90f;

        [Header("Margem")]
        [Tooltip("Largura, em metros, em que o relevo é suavizado em volta do lago (da margem para fora), para não haver paredão na margem.")]
        [Min(5f)] public float shoreReach = 45f;

        [Tooltip("Inclinação da margem logo acima da água (metros de altura por metro de distância).")]
        [Range(0.02f, 0.5f)] public float bankSlope = 0.12f;

        [Header("Faixas em volta do lago (metros a partir da margem)")]
        [Tooltip("De 0 até aqui: só areia. Nem palmeira nem arbusto.")]
        [Min(0f)] public float bareSandMeters = 15f;

        [Tooltip("Depois da areia nua, por esta largura: SÓ palmeiras e arbustos, com o chão todo de areia.")]
        [Min(0f)] public float palmMeters = 30f;

        [Tooltip("Depois da faixa só das palmeiras, por esta largura: árvores, arbustos e palmeiras juntos, em manchas, e o chão vai de areia para a textura da floresta (sem fronteira nítida). Mais perto do lago, mais palmeiras; mais longe, mais árvores.")]
        [Min(1f)] public float mixMeters = 30f;

        [Tooltip("Tamanho, em metros, das manchas de palmeiras e de árvores na faixa de mistura. Uma palmeira nunca nasce junto de uma árvore: cada mancha é de um tipo só.")]
        [Min(8f)] public float patchMeters = 32f;

        [Tooltip("Folga, em metros, entre uma mancha de palmeiras e uma de árvores (nada nasce na faixa entre elas). Evita palmeira dentro de árvore.")]
        [Min(0f)] public float patchGapMeters = 9f;

        [Header("Debaixo d'água")]
        [Tooltip("Cor da neblina e do ambiente quando a câmera está debaixo d'água.")]
        public Color underwaterColor = new Color(0.04f, 0.15f, 0.17f);

        [Tooltip("Até que distância, em metros, se enxerga debaixo d'água (a neblina cobre tudo ali).")]
        [Min(4f)] public float underwaterVisibility = 22f;

        [Header("Visual")]
        [Tooltip("Material da água (shader PROCEDURAL_GAME/Water). Criado por PROCEDURAL_GAME > Create Procedural World.")]
        public Material waterMaterial;

        [Tooltip("Cor da água rasa (junto da margem).")]
        public Color shallowColor = new Color(0.20f, 0.27f, 0.24f);

        [Tooltip("Cor da água funda.")]
        public Color deepColor = new Color(0.02f, 0.05f, 0.06f);

        [Tooltip("Textura da areia da margem e das faixas em volta do lago (cor que se repete).")]
        public Texture2D sandTexture;

        [Tooltip("Mapa de normal da areia da margem. Só é usado com 'Qualidade do solo' ligada no menu Desempenho. Gerado pelo menu PROCEDURAL_GAME > Ground > Generate Normal Maps.")]
        public Texture2D sandNormal;

        [Tooltip("Textura do fundo do lago (areia lisa e escura). O fundo mistura a areia da margem com esta.")]
        public Texture2D bedTexture;

        [Tooltip("Mapa de normal do fundo do lago (ver 'Sand Normal').")]
        public Texture2D bedNormal;

        /// <summary>Distância da margem (m) em que acaba a faixa SÓ de palmeiras e arbustos (o chão ainda é todo de areia).</summary>
        public float PalmOnlyEnd => bareSandMeters + palmMeters;

        /// <summary>Distância da margem (m) em que acaba a mistura: dali para fora é a floresta normal.</summary>
        public float MixEnd => PalmOnlyEnd + mixMeters;

        /// <summary>Distância da margem (m) a partir da qual o lago não influencia mais nada.</summary>
        public float TotalReach => Mathf.Max(shoreReach, MixEnd + patchMeters);
    }
}
