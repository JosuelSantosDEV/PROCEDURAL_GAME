using System;
using UnityEngine;

namespace PROCEDURAL_GAME.World
{
    /// <summary>
    /// "Visual" de um bioma: cores do terreno e atmosfera (neblina, luz, céu).
    /// Hoje o mundo usa um perfil só; os biomas por direção (deserto, floresta, campo, cidade)
    /// serão perfis diferentes misturados conforme a posição do Player.
    /// Os valores padrão abaixo são os do campo. Crie um novo pelo menu Assets > Create > PROCEDURAL_GAME > Biome Profile.
    /// </summary>
    [CreateAssetMenu(menuName = "PROCEDURAL_GAME/Biome Profile", fileName = "NewBiomeProfile")]
    public class BiomeProfile : ScriptableObject
    {
        [Header("Menu")]
        [Tooltip("Nome mostrado no menu inicial. Vazio = nome do arquivo.")]
        public string displayName = "";

        [Tooltip("Frase curta mostrada no menu abaixo do nome.")]
        [TextArea(1, 3)] public string description = "";

        /// <summary>Nome para mostrar ao jogador.</summary>
        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;

        [Header("Terreno: cores")]
        [Tooltip("Partes baixas do relevo (vales): lama, terra úmida.")]
        public Color lowColor = new Color(0.17f, 0.19f, 0.10f);

        [Tooltip("Altura média: o chão principal (grama, terra).")]
        public Color grassColor = new Color(0.27f, 0.35f, 0.17f);

        [Tooltip("Partes altas do relevo.")]
        public Color highColor = new Color(0.45f, 0.45f, 0.36f);

        [Tooltip("Encostas íngremes.")]
        public Color rockColor = new Color(0.34f, 0.34f, 0.33f);

        [Header("Terreno: regras de mistura")]
        [Tooltip("Até essa altura (0 = nível mais baixo, 1 = mais alto) a cor passa de 'Low' para 'Grass'.")]
        [Range(0f, 1f)] public float lowHeightEnd = 0.30f;

        [Tooltip("A partir dessa altura a cor passa de 'Grass' para 'High'.")]
        [Range(0f, 1f)] public float highHeightStart = 0.65f;

        [Tooltip("Inclinação em que a rocha começa a aparecer (1 = chão plano, 0 = parede). Menor = só aparece em encostas mais íngremes.")]
        [Range(0f, 1f)] public float rockSlopeStart = 0.80f;

        [Tooltip("Inclinação em que o chão é 100% rocha. Tem que ser menor que 'Rock Slope Start'.")]
        [Range(0f, 1f)] public float rockSlopeFull = 0.62f;

        [Tooltip("Variação aleatória de brilho, para o chão não ficar uma cor chapada.")]
        [Range(0f, 0.5f)] public float colorVariation = 0.15f;

        /// <summary>Textura de chão de uma zona do mundo (ex.: uma estação do ano) e o mapa de normal que a acompanha.</summary>
        [Serializable]
        public struct GroundLayer
        {
            [Tooltip("Id da zona (campo 'Id' de uma entrada de 'Zones' do perfil de árvores, ex.: Autumn, Spring, Summer).")]
            public string zoneId;

            [Tooltip("Textura de cor que se repete. Sem emenda (tileable). No celular use 512 px (o importador em Assets/Project/Textures/Ground já faz isso).")]
            public Texture2D albedo;

            [Tooltip("Mapa de normal (relevo) que acompanha a textura. Só é usado com 'Qualidade do solo' ligada no menu Desempenho. Gerado pelo menu PROCEDURAL_GAME > Ground > Generate Normal Maps.")]
            public Texture2D normal;
        }

        public const int MaxGroundLayers = 3;

        [Header("Textura do chão (opcional)")]
        [Tooltip("Uma textura de chão por zona do mundo (até 3). O chão mistura suavemente as texturas na fronteira entre zonas, usando o mesmo mapa de zonas das árvores. Vazio = usa 'Ground Texture' (uma só) ou só a cor de vértice.")]
        public GroundLayer[] groundLayers;

        [Tooltip("Largura aproximada, em metros, da faixa de transição entre texturas de zonas vizinhas. Maior = mistura mais longa e suave.")]
        [Min(1f)] public float groundBlendMeters = 30f;

        [Tooltip("Textura única de chão, usada só quando 'Ground Layers' está vazio.")]
        public Texture2D groundTexture;

        [Tooltip("Lado, em metros, que uma repetição da textura cobre. Menor = detalhe mais fino e repetição mais visível.")]
        [Min(0.5f)] public float groundTileMeters = 4f;

        [Tooltip("Quanto da textura vale no chão: 0 = só a cor de vértice; 1 = a textura toda (a rocha das encostas nunca recebe textura).")]
        [Range(0f, 1f)] public float groundTextureStrength = 1f;

        [Tooltip("Quanto a cor de vértice (altura e variação de brilho) ainda modula a textura: 0 = textura pura; 1 = acompanha a cor de vértice por inteiro.")]
        [Range(0f, 1f)] public float groundVertexInfluence = 0.5f;

        [Header("Atmosfera")]
        [Tooltip("Cor da neblina. O céu usa a mesma cor, então o horizonte some sem emenda.")]
        public Color fogColor = new Color(0.55f, 0.57f, 0.60f);

        [Tooltip("NÃO USADO em jogo: a densidade da neblina agora vem da distância de visão escolhida no menu Desempenho (Perto/Médio/Longe), de modo que a neblina cubra tudo nessa distância (World/ViewDistance). Fica só para o editor.")]
        [Min(0f)] public float fogDensity = 0.014f;

        [Tooltip("Luz que vem de todo lugar (sombras não ficam pretas). Mais escuro = mais sombrio.")]
        public Color ambientColor = new Color(0.30f, 0.32f, 0.35f);

        [Tooltip("Cor da luz direcional (sol/lua).")]
        public Color lightColor = new Color(0.80f, 0.82f, 0.85f);

        [Min(0f)] public float lightIntensity = 0.8f;

        [Header("Distância de visão")]
        [Tooltip("NÃO USADO em jogo: a distância de visão vem do menu Desempenho (Perto/Médio/Longe). Fica só por compatibilidade.")]
        [Range(0, 12)] public int viewRadius = 0;

        [Header("Sombras")]
        [Tooltip("Distância máxima das sombras, em metros, em cada nível do menu: Baixa, Média e Alta. Menor = sombra mais nítida e mais barata (o mapa de sombras tem resolução fixa, 512 px no URP-Balanced). Em biomas de neblina densa não adianta passar de onde a neblina esconde tudo.")]
        public Vector3 shadowDistances = new Vector3(20f, 30f, 40f);

        /// <summary>Distância de sombra do nível de menu 1 (Baixa) a 3 (Alta); 0 se desligadas.</summary>
        public float GetShadowDistance(int level)
        {
            switch (level)
            {
                case 1: return Mathf.Max(1f, shadowDistances.x);
                case 2: return Mathf.Max(1f, shadowDistances.y);
                case 3: return Mathf.Max(1f, shadowDistances.z);
                default: return 0f;
            }
        }

        [Header("Objetos")]
        [Tooltip("O que nasce nesse bioma (árvores, pedras...). Cada item é um Scatter Profile. Lido uma vez, ao dar Play.")]
        public ScatterProfile[] scatter;

        /// <summary>O perfil de objetos que define as zonas do mundo (o primeiro com grupos e zonas, ex.: as árvores); nulo se não há.</summary>
        public ScatterProfile FindZoneProfile()
        {
            if (scatter == null) return null;

            foreach (ScatterProfile profile in scatter)
            {
                if (profile != null && profile.zoneSource == null && profile.UsesZones) return profile;
            }

            return null;
        }
    }
}
