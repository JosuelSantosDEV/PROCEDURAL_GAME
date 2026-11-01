using UnityEngine;

namespace PROCEDURAL_GAME.World
{
    /// <summary>Como o objeto colide com o Player. Os colisores originais dos prefabs são sempre removidos (malhas de colisão pesam).</summary>
    public enum ScatterCollision
    {
        [Tooltip("Sem colisão: o Player atravessa. Mais barato.")] None,
        [Tooltip("Cápsula fina de tronco (árvores): só o tronco bloqueia, a copa não.")] Trunk,
        [Tooltip("Caixa ao redor do objeto (pedras).")] Box
    }

    /// <summary>
    /// Regras para espalhar um tipo de objeto (árvores, pedras, depois recursos e inimigos) pelos chunks.
    /// As posições são sorteadas só com seed + coordenada do chunk: voltar a um lugar traz os mesmos objetos.
    /// A lista de perfis usada fica no <see cref="BiomeProfile"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "PROCEDURAL_GAME/Scatter Profile", fileName = "NewScatterProfile")]
    public class ScatterProfile : ScriptableObject
    {
        [Header("O que espalhar")]
        [Tooltip("Variações sorteadas ao acaso. Prefabs vazios são ignorados.")]
        public GameObject[] prefabs;

        [Tooltip("Número diferente para cada perfil: perfis com o mesmo número sortearam posições parecidas.")]
        public int salt = 1;

        [Tooltip("Nome do controle de densidade no menu de Desempenho (ex.: Árvores, Arbustos, Rochas). Perfis com o mesmo nome compartilham o controle. Vazio = não aparece no menu.")]
        public string category = "";

        [Header("Quantidade e posição")]
        [Tooltip("Tentativas por chunk (32 x 32 m). É o máximo de objetos: tentativas em lugar proibido (muito inclinado, alto demais, perto de outro) são descartadas.")]
        [Min(0)] public int attemptsPerChunk = 12;

        [Tooltip("Distância mínima entre dois objetos do mesmo perfil, em metros. Não vale entre chunks vizinhos.")]
        [Min(0f)] public float minSpacing = 4f;

        [Tooltip("Agrupamento: 0 = espalhado por igual; 1 = só em manchas (bosques, pedreiras) com clareiras entre elas.")]
        [Range(0f, 1f)] public float clustering = 0.5f;

        [Tooltip("Tamanho das manchas do agrupamento, em metros.")]
        [Min(5f)] public float clusterSize = 60f;

        [Header("Onde pode nascer")]
        [Tooltip("Inclinação máxima do terreno, em graus.")]
        [Range(0f, 90f)] public float maxSlope = 30f;

        [Tooltip("Altura mínima, de 0 (ponto mais baixo do relevo) a 1 (mais alto).")]
        [Range(0f, 1f)] public float minHeight = 0f;

        [Tooltip("Altura máxima, de 0 a 1.")]
        [Range(0f, 1f)] public float maxHeight = 1f;

        [Header("Forma")]
        [Min(0.01f)] public float minScale = 0.8f;
        [Min(0.01f)] public float maxScale = 1.3f;

        [Tooltip("Quanto o objeto acompanha a inclinação do chão. 0 = sempre em pé (árvores); 1 = deitado no chão (pedras).")]
        [Range(0f, 1f)] public float alignToSurface = 0f;

        [Tooltip("Quanto o objeto afunda no chão, em metros por unidade de escala. Esconde a base e evita 'flutuar' em encostas.")]
        [Min(0f)] public float sinkDepth = 0.1f;

        [Header("Colisão")]
        public ScatterCollision collision = ScatterCollision.None;

        [Tooltip("Raio do tronco, em metros (só para 'Trunk').")]
        [Min(0.05f)] public float trunkRadius = 0.35f;
    }
}
