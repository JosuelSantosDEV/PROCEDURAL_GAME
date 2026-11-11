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
        [Tooltip("Lado, em metros, da célula do mundo que pode ter UM lago. Maior = lagos mais raros. Água é recurso escasso: o Player precisa explorar para achar.")]
        [Min(150f)] public float cellSize = 520f;

        [Tooltip("Chance de uma célula ter lago (0 a 1). Com 520 m e 0,3: em média um lago a cada ~850 m de caminhada em linha reta.")]
        [Range(0f, 1f)] public float chance = 0.3f;

        [Tooltip("Raio médio do lago, em metros (mínimo e máximo sorteados por lago). O contorno é irregular: o raio varia cerca de ±40% conforme o ângulo.")]
        public Vector2 radiusRange = new Vector2(28f, 44f);

        [Tooltip("Profundidade no centro, em metros. Fundo em forma de tigela, com margem suave. 4 a 5 m dá para nadar por baixo.")]
        [Min(0.5f)] public float maxDepth = 9f;

        [Tooltip("Profundidade da depressão: quanto a água fica ABAIXO do terreno em volta. O terreno desce suavemente até a água (ilustração depressao_desejada). Nível da água = altura natural no centro do lago menos isto. Cada lago tem o próprio nível.")]
        [Min(0.3f)] public float levelOffset = 6f;

        [Tooltip("Sem lago a esta distância (m) do ponto (0, 0): o Player nunca começa perto da água. Alto de propósito: é preciso explorar.")]
        [Min(0f)] public float clearRadius = 250f;

        [Header("Margem")]
        [Tooltip("Largura da depressão, em metros a partir da margem: o terreno desce suavemente de fora até a água ao longo desta distância.")]
        [Min(8f)] public float depressionMeters = 64f;

        [Tooltip("Até que distância da margem (m) o relevo ainda é suavizado de volta para a altura natural (depois da depressão, a mistura vai até aqui).")]
        [Min(5f)] public float shoreReach = 100f;

        [Header("Faixas em volta do lago (metros a partir da margem)")]
        [Tooltip("De 0 até aqui: só areia, nem palmeira nem arbusto.")]
        [Min(0f)] public float bareSandMeters = 16f;

        [Tooltip("Depois da areia nua, por esta largura: SÓ palmeiras e arbustos, com o chão todo de areia.")]
        [Min(0f)] public float palmMeters = 30f;

        [Tooltip("Depois da faixa só das palmeiras, por esta largura: árvores, arbustos e palmeiras juntos, em manchas, e o chão vai de areia para a textura da floresta (sem fronteira nítida). Mais perto do lago, mais palmeiras; mais longe, mais árvores.")]
        [Min(1f)] public float mixMeters = 30f;

        [Tooltip("Depois da mistura, por esta largura: ainda é a floresta normal (chão de floresta), mas com manchas de palmeiras que vão ficando mais raras para fora. As palmeiras começam a aparecer aqui, DENTRO da floresta, bem antes da areia.")]
        [Min(0f)] public float introMeters = 50f;

        [Tooltip("Chance (0 a 1) de uma mancha ser de palmeiras no limite entre a mistura e a floresta (cai a 0 no fim da faixa acima). Maior = mais palmeiras entre as árvores.")]
        [Range(0f, 1f)] public float introPalmShare = 0.45f;

        [Tooltip("Tamanho, em metros, das manchas de palmeiras e de árvores na faixa de mistura. Uma palmeira nunca nasce junto de uma árvore: cada mancha é de um tipo só.")]
        [Min(8f)] public float patchMeters = 20f;

        [Tooltip("Folga, em metros, entre uma mancha de palmeiras e uma de árvores (nada nasce na faixa entre elas). Evita palmeira dentro de árvore.")]
        [Min(0f)] public float patchGapMeters = 3.5f;

        [Header("Debaixo d'água")]
        [Tooltip("Cor da neblina e do ambiente quando a câmera está debaixo d'água.")]
        public Color underwaterColor = new Color(0.04f, 0.15f, 0.17f);

        [Tooltip("Até que distância, em metros, se enxerga debaixo d'água (a neblina cobre tudo ali).")]
        [Min(4f)] public float underwaterVisibility = 22f;

        [Header("Visual")]
        [Tooltip("Material da água (shader PROCEDURAL_GAME/Water). Criado por PROCEDURAL_GAME > Create Procedural World.")]
        public Material waterMaterial;

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

        /// <summary>Distância da margem (m) em que acaba a mistura (o chão já é o da floresta; ainda há palmeiras, mais raras).</summary>
        public float MixEnd => PalmOnlyEnd + mixMeters;

        /// <summary>Distância da margem (m) em que acabam as palmeiras: dali para fora é a floresta normal.</summary>
        public float IntroEnd => MixEnd + introMeters;

        /// <summary>Distância da margem (m) a partir da qual o lago não influencia mais nada.</summary>
        public float TotalReach => Mathf.Max(shoreReach, IntroEnd + patchMeters);

        /// <summary>Distância da margem (m) em que a mistura da depressão com o relevo natural termina (nunca menor que a depressão + 5 m).</summary>
        public float BlendEnd => Mathf.Max(shoreReach, depressionMeters + 5f);
    }
}
