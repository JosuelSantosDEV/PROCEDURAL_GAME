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

        [Header("Atmosfera")]
        [Tooltip("Cor da neblina. O céu usa a mesma cor, então o horizonte some sem emenda.")]
        public Color fogColor = new Color(0.55f, 0.57f, 0.60f);

        [Tooltip("Densidade da neblina. Maior = enxerga menos. Distância em que a neblina esconde 95% ≈ 1,7 / densidade (0,014 → ~125 m).")]
        [Min(0f)] public float fogDensity = 0.014f;

        [Tooltip("Luz que vem de todo lugar (sombras não ficam pretas). Mais escuro = mais sombrio.")]
        public Color ambientColor = new Color(0.30f, 0.32f, 0.35f);

        [Tooltip("Cor da luz direcional (sol/lua).")]
        public Color lightColor = new Color(0.80f, 0.82f, 0.85f);

        [Min(0f)] public float lightIntensity = 0.8f;

        [Header("Distância de visão")]
        [Tooltip("Raio de carregamento desse bioma, em chunks. 0 = usa o 'View Radius' do World. Biomas fechados (floresta) podem usar menos, com neblina mais densa, e carregam bem menos objetos. Lido uma vez, ao dar Play. Mantenha a neblina ≈ Raio × Chunk Size.")]
        [Range(0, 12)] public int viewRadius = 0;

        [Header("Objetos")]
        [Tooltip("O que nasce nesse bioma (árvores, pedras...). Cada item é um Scatter Profile. Lido uma vez, ao dar Play.")]
        public ScatterProfile[] scatter;
    }
}
