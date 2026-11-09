using System;
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

    /// <summary>Papel do objeto nas faixas em volta dos lagos (ver <see cref="LakeSettings"/>). Só vale em biomas com lagos.</summary>
    public enum LakeBand
    {
        [Tooltip("Só não nasce dentro da água (pedras, por exemplo).")] Ignore,
        [Tooltip("Árvores normais: não nascem perto do lago (areia nua e faixa das palmeiras); voltam aos poucos mais longe.")] NormalTrees,
        [Tooltip("Arbustos: não nascem na areia nua; nascem na faixa das palmeiras e na floresta.")] Shrubs,
        [Tooltip("Palmeiras: só nascem na faixa das palmeiras (depois da areia nua).")] Palms
    }

    /// <summary>O que o objeto é para o jogador. Coletáveis entram na lista de <see cref="PickupRegistry"/> e não voltam ao recriar o chunk.</summary>
    public enum PickupKind
    {
        [Tooltip("Cenário: não dá para pegar.")] None,
        [Tooltip("Comida: o botão vira COMER por perto.")] Food
    }

    /// <summary>
    /// Regras para espalhar um tipo de objeto (árvores, pedras, depois recursos e inimigos) pelos chunks.
    /// As posições são sorteadas só com seed + coordenada do chunk: voltar a um lugar traz os mesmos objetos.
    /// A lista de perfis usada fica no <see cref="BiomeProfile"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "PROCEDURAL_GAME/Scatter Profile", fileName = "NewScatterProfile")]
    public class ScatterProfile : ScriptableObject
    {
        /// <summary>Um prefab de um grupo e o pacote de onde veio (ex.: Autumn, Spring, Summer, Misc).</summary>
        [Serializable]
        public struct GroupVariant
        {
            public GameObject prefab;
            public string pack;
        }

        /// <summary>
        /// Um tipo de objeto com várias variações (ex.: todas as "young1" de verão, primavera e outono) e os ajustes
        /// que valem para o tipo inteiro: altura em relação ao chão e largura do colisor.
        /// </summary>
        [Serializable]
        public class PrefabGroup
        {
            [Tooltip("Identificador estável (ASCII) do tipo, ex.: young1. Não mude: é a chave dos ajustes de desenvolvimento.")]
            public string id;

            [Tooltip("Nome mostrado nas ferramentas.")]
            public string displayName;

            [Tooltip("Altura em relação ao terreno, em metros (absoluto). Negativo = abaixo do chão: -1 enterra 1 metro.")]
            public float elevation;

            [Tooltip("Multiplicador do raio do colisor de tronco (1 = o 'Trunk Radius' do perfil; 1,1 = 10% mais largo).")]
            [Min(0.1f)] public float colliderScale = 1f;

            [Tooltip("Peso do tipo no sorteio: todos os tipos valem o mesmo por padrão (1), qualquer que seja o número de variações. 0,5 = metade da frequência dos demais; 2 = o dobro.")]
            [Min(0.01f)] public float weight = 1f;

            [Tooltip("Preferência do tipo (1 a 3) na mistura por preferência do perfil: a fatia de cada preferência é dividida entre os tipos dela. Só vale com 'Mix By Preference' ligado.")]
            [Range(1, 3)] public int preference = 2;

            public GroupVariant[] variants;
        }

        /// <summary>Uma zona do mundo (ex.: uma estação do ano) e os pacotes cujas variações podem nascer nela.</summary>
        [Serializable]
        public class SeasonZone
        {
            [Tooltip("Nome da zona (ex.: Autumn).")]
            public string id;

            [Tooltip("Pacotes (o campo 'Pack' das variações) que nascem nesta zona. Pacotes parecidos podem dividir uma zona (ex.: Summer e Misc).")]
            public string[] packs;
        }

        /// <summary>Variação já resolvida (lista única, na ordem dos grupos) usada na geração.</summary>
        public struct Variant
        {
            public GameObject prefab;

            /// <summary>Índice em <see cref="groups"/>; -1 = prefab solto (sem ajustes por tipo).</summary>
            public int group;

            public string pack;

            /// <summary>Zonas em que a variação nasce: um bit por zona de <see cref="ActiveZones"/> (bit 0 = primeira). -1 = todas (pacote sem zona).</summary>
            public int zoneMask;
        }

        [Header("Jogo")]
        public PickupKind pickupKind = PickupKind.None;

        [Tooltip("Faz parte do jogo, não do cenário (ex.: comida): nem a qualidade automática nem o menu de Desempenho reduzem a quantidade. Só vale no sorteio livre (sem 'Spacing By Density').")]
        public bool essential;

        [Header("O que espalhar")]
        [Tooltip("Variações sorteadas ao acaso. Só vale quando 'Groups' está vazio. Prefabs vazios são ignorados.")]
        public GameObject[] prefabs;

        [Tooltip("Variações organizadas por tipo (cada tipo com sua altura e colisor). Se tiver itens, substitui a lista 'Prefabs'. Gerado pelo menu PROCEDURAL_GAME > Rebuild Tree Groups.")]
        public PrefabGroup[] groups;

        [Tooltip("Número diferente para cada perfil: perfis com o mesmo número sortearam posições parecidas.")]
        public int salt = 1;

        [Tooltip("Identificador interno da categoria (ex.: trees, shrubs, rocks): minúsculas, sem acento nem espaço. É a chave em que a escolha do jogador é gravada, então não mude depois de publicar. Perfis com o mesmo id compartilham o controle de densidade. Vazio = não aparece no menu de Desempenho.")]
        public string categoryId = "";

        [Tooltip("Nome mostrado no menu de Desempenho (ex.: Árvores). Pode ter acento e ser traduzido sem perder as escolhas salvas. Vazio = mostra o id.")]
        public string category = "";

        [Header("Quantidade e posição")]
        [Tooltip("Tentativas por chunk (32 x 32 m). É o máximo de objetos: tentativas em lugar proibido (muito inclinado, alto demais, perto de outro) são descartadas.")]
        [Min(0f)] public float attemptsPerChunk = 12f;

        [Tooltip("Distância mínima entre dois objetos do mesmo perfil, em metros. Não vale entre chunks vizinhos. Ignorada com 'Spacing By Density' ligado.")]
        [Min(0f)] public float minSpacing = 4f;

        [Tooltip("Distribui os objetos numa GRADE quadrada (cada um com 4 vizinhos à mesma distância, como uma matriz) em vez de sorteá-los soltos. O lado da grade é a raiz da área por objeto: mais densidade = objetos mais perto; menos densidade = mais longe. Ignora 'Min Spacing'.")]
        public bool spacingByDensity = false;

        [Tooltip("Desvio aleatório de cada objeto dentro da sua célula da grade, como fração do lado (0 = matriz perfeita; 0,15 = leve desvio natural; máx. 0,45). A distância mínima entre dois objetos é lado x (1 - 2 x desvio).")]
        [Range(0f, 0.45f)] public float spacingJitter = 0.15f;

        [Header("Zonas (só com grupos)")]
        [Tooltip("Perfil cujas zonas (lista, tamanho e sorteio) este perfil usa, para dividir o mundo exatamente igual a ele (ex.: arbustos seguindo as zonas das árvores). Vazio = usa as próprias.")]
        public ScatterProfile zoneSource;

        [Tooltip("Divide o mundo em zonas; cada zona usa só os pacotes de UMA entrada desta lista (ex.: uma estação), sem misturar. Vazio = sem zonas, todos os pacotes juntos.")]
        public SeasonZone[] zones;

        [Tooltip("Tamanho típico de uma zona, em metros: a área é de aproximadamente este valor ao quadrado (160 m = ~25.000 m²). As zonas têm formato irregular e a troca entre elas é seca, sem árvores de zonas diferentes intercaladas.")]
        [Min(20f)] public float zoneSize = 160f;

        [Header("Mistura de tipos (só com grupos)")]
        [Tooltip("Liga o sorteio por preferência: cada tipo tem uma preferência (1 a 3) e as fatias abaixo dizem quanto de cada preferência aparece, variando entre floresta baixa e alta conforme o lugar. Desligado = todos os tipos valem o seu 'Weight'.")]
        public bool mixByPreference = false;

        [Tooltip("Fatia de cada preferência (1, 2, 3) na floresta BAIXA. Os valores são relativos (não precisam somar 1).")]
        public Vector3 lowForestShares = new Vector3(0.5f, 0.3f, 0.2f);

        [Tooltip("Fatia de cada preferência (1, 2, 3) na floresta ALTA.")]
        public Vector3 highForestShares = new Vector3(0.35f, 0.35f, 0.3f);

        [Tooltip("Tamanho das manchas de floresta baixa/alta, em metros. Maior = trechos mais longos de cada tipo.")]
        [Min(20f)] public float forestMixSize = 180f;

        [Tooltip("Nitidez da transição entre floresta baixa e alta. Maior = trechos mais definidos; menor = mistura gradual.")]
        [Range(1f, 8f)] public float forestMixContrast = 3f;

        [Tooltip("Inclina o mundo para um lado: -1 = só floresta baixa; 0 = metade/metade; +1 = só floresta alta.")]
        [Range(-1f, 1f)] public float forestMixBias = 0f;

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

        [Tooltip("Papel nas faixas em volta dos lagos (só em biomas com lagos): árvores normais, arbustos ou palmeiras.")]
        public LakeBand lakeBand = LakeBand.Ignore;

        [Header("Forma")]
        [Min(0.01f)] public float minScale = 0.8f;
        [Min(0.01f)] public float maxScale = 1.3f;

        [Tooltip("Quanto o objeto acompanha a inclinação do chão. 0 = sempre em pé (árvores); 1 = deitado no chão (pedras).")]
        [Range(0f, 1f)] public float alignToSurface = 0f;

        [Tooltip("Afundamento fixo no chão, em metros por unidade de escala (soma com a altura do grupo, se houver).")]
        [Min(0f)] public float sinkDepth = 0.1f;

        [Header("Sombras")]
        [Tooltip("A partir de qual nível de sombras do jogo este objeto projeta sombra: 0 = nunca; 1 = Baixa, Média e Alta; 2 = Média e Alta; 3 = só Alta. -1 = não definido (vale 0; o menu Create Procedural World preenche).")]
        [Range(-1, 3)] public int castShadowsFromLevel = -1;

        [Header("Colisão")]
        public ScatterCollision collision = ScatterCollision.None;

        [Tooltip("Raio do tronco, em metros (só para 'Trunk'). Em perfis com grupos, o multiplicador de cada grupo é aplicado sobre este valor.")]
        [Min(0.05f)] public float trunkRadius = 0.35f;

        // Lista única usada na geração. Não serializada: é refeita sozinha quando falta (recarga de scripts, edição no Inspector).
        [NonSerialized] private Variant[] variants;

        // Por grupo: primeira variação em 'Variants' e quantas são (as variações de um grupo ficam seguidas).
        [NonSerialized] private int[] groupFirst;
        [NonSerialized] private int[] groupLength;

        /// <summary>Nível mínimo de sombras para este objeto projetar sombra (0 = nunca).</summary>
        public int ShadowFromLevel => Mathf.Max(0, castShadowsFromLevel);

        public bool HasGroups => groups != null && groups.Length > 0;

        // Quem define as zonas: o perfil indicado em 'zoneSource' ou este mesmo. Um nível só (a origem não é seguida de novo).
        private ScatterProfile ZoneProfile => zoneSource != null ? zoneSource : this;

        /// <summary>Zonas em vigor para este perfil (as próprias ou as do 'zoneSource').</summary>
        public SeasonZone[] ActiveZones => ZoneProfile.zones;

        /// <summary>Tamanho típico das zonas, em metros.</summary>
        public float ZoneSize => ZoneProfile.zoneSize;

        /// <summary>Número que embaralha o sorteio das zonas. Perfis com a mesma origem têm o mesmo mapa de zonas.</summary>
        public int ZoneSalt => ZoneProfile.salt;

        /// <summary>Quantas zonas o mundo tem para este perfil (1 = sem zonas).</summary>
        public int ZoneCount => UsesZones ? ActiveZones.Length : 1;

        /// <summary>Verdadeiro se o perfil divide o mundo em zonas (precisa de grupos e de ao menos uma zona).</summary>
        public bool UsesZones => HasGroups && ActiveZones != null && ActiveZones.Length > 0;

        /// <summary>Todas as variações possíveis, na ordem estável usada pelo sorteio (o índice é parte do determinismo).</summary>
        public Variant[] Variants => variants ?? (variants = BuildVariants());

        /// <summary>Índice em <see cref="Variants"/> da primeira variação do grupo.</summary>
        public int GetGroupFirst(int group)
        {
            Variant[] unused = Variants;
            return groupFirst[group];
        }

        /// <summary>Quantas variações o grupo tem.</summary>
        public int GetGroupLength(int group)
        {
            Variant[] unused = Variants;
            return groupLength[group];
        }

        /// <summary>Peso do grupo no sorteio de tipos.</summary>
        public float GetGroupWeight(int group) => IsValidGroup(group) ? Mathf.Max(0.01f, groups[group].weight) : 1f;

        /// <summary>Preferência (1 a 3) do grupo na mistura por preferência.</summary>
        public int GetPreference(int group) => IsValidGroup(group) ? Mathf.Clamp(groups[group].preference, 1, 3) : 2;

        /// <summary>Altura (m) do grupo em relação ao terreno; 0 para prefab solto.</summary>
        public float GetElevation(int group) => IsValidGroup(group) ? groups[group].elevation : 0f;

        /// <summary>Multiplicador do colisor do grupo; 1 para prefab solto.</summary>
        public float GetColliderScale(int group) => IsValidGroup(group) ? Mathf.Max(0.1f, groups[group].colliderScale) : 1f;

        private bool IsValidGroup(int group) => groups != null && group >= 0 && group < groups.Length;

        private Variant[] BuildVariants()
        {
            System.Collections.Generic.List<Variant> list = new System.Collections.Generic.List<Variant>();

            if (HasGroups)
            {
                groupFirst = new int[groups.Length];
                groupLength = new int[groups.Length];

                for (int g = 0; g < groups.Length; g++)
                {
                    groupFirst[g] = list.Count;

                    GroupVariant[] source = groups[g]?.variants;
                    if (source == null) continue;

                    foreach (GroupVariant item in source)
                    {
                        if (item.prefab != null) list.Add(new Variant { prefab = item.prefab, group = g, pack = item.pack });
                    }

                    groupLength[g] = list.Count - groupFirst[g];
                }
            }
            else if (prefabs != null)
            {
                foreach (GameObject prefab in prefabs)
                {
                    if (prefab != null) list.Add(new Variant { prefab = prefab, group = -1, pack = null });
                }
            }

            Variant[] result = list.ToArray();
            for (int i = 0; i < result.Length; i++) result[i].zoneMask = ZoneMaskOfPack(result[i].pack);
            return result;
        }

        // Um pacote pode nascer em várias zonas (ex.: os arbustos "Green" na primavera e no verão).
        private int ZoneMaskOfPack(string pack)
        {
            SeasonZone[] active = ActiveZones;
            if (active == null || string.IsNullOrEmpty(pack)) return -1;

            int mask = 0;
            for (int z = 0; z < active.Length && z < 31; z++)
            {
                string[] packs = active[z]?.packs;
                if (packs == null) continue;

                foreach (string candidate in packs)
                {
                    if (string.Equals(candidate, pack, StringComparison.OrdinalIgnoreCase)) mask |= 1 << z;
                }
            }

            return mask != 0 ? mask : -1;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Mexeu nas listas no Inspector: refaz a lista única na próxima geração.
            variants = null;
        }
#endif
    }
}
