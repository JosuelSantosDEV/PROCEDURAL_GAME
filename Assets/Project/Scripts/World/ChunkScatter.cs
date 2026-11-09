using System.Collections.Generic;
using PROCEDURAL_GAME.Core;
using Unity.Mathematics;
using UnityEngine;
using Random = Unity.Mathematics.Random;

namespace PROCEDURAL_GAME.World
{
    /// <summary>Um objeto a colocar: qual variação do prefab e onde.</summary>
    public struct ScatterPlacement
    {
        /// <summary>Índice em <see cref="ScatterProfile.Variants"/>.</summary>
        public int VariantIndex;
        public Vector3 Position;
        public Quaternion Rotation;
        public float Scale;

        /// <summary>Multiplicador do colisor do grupo da variação (1 = padrão do perfil).</summary>
        public float ColliderScale;
    }

    /// <summary>
    /// Decide onde ficam os objetos de um chunk. É uma função pura de (seed, chunk, perfil): não guarda nada,
    /// então o mesmo chunk sempre recebe os mesmos objetos, mesmo depois de ser descartado e recriado.
    /// Só calcula posições; criar os GameObjects é trabalho do <see cref="ScatterSpawner"/>.
    /// </summary>
    public sealed class ChunkScatter
    {
        // Faixa em que o ruído de agrupamento passa de "nunca" a "sempre" (o ruído fica em torno de -0,9..0,9).
        private const float ClusterMin = -0.3f;
        private const float ClusterMax = 0.5f;

        // Distância usada para medir a inclinação do terreno, em metros.
        private const float SlopeStep = 1f;

        // Menor lado de célula da grade, em metros (densidade altíssima não pode gerar milhares de células por chunk).
        private const float MinGridCell = 1f;

        // Desvio máximo do objeto dentro da célula, como fração do lado. Abaixo de 0,5 o ponto nunca sai da própria célula.
        private const float MaxGridJitter = 0.45f;

        // Deslocamento do ruído de mistura baixa/alta para não repetir o desenho do ruído de agrupamento.
        private const int ForestMixSaltOffset = 7919;


        private readonly TerrainNoise terrain;
        private readonly int seed;
        private readonly float chunkSize;
        private readonly float spawnClearSqr;
        private readonly LakeMap lakes;

        // Reaproveitadas a cada chamada para não gerar lixo de memória.
        private readonly List<Vector2> accepted = new List<Vector2>();

        // Por zona e por tipo: variações que podem nascer (da zona e liberadas pela calibração); por zona, soma dos pesos
        // dos tipos em cada preferência (índices 1 a 3); e o peso final de cada tipo no ponto sorteado.
        private List<int>[][] eligible = System.Array.Empty<List<int>[]>();
        private float[][] tierWeightSum = System.Array.Empty<float[]>();
        private float[] groupWeights = System.Array.Empty<float>();

        // Variações liberadas pela ferramenta de calibração (só usada quando ScatterTuning.HasFilter).
        private readonly List<int> enabledVariants = new List<int>();
        private bool[] enabledFlags = System.Array.Empty<bool>();

        public ChunkScatter(TerrainNoise terrain, int seed, float chunkSize, float spawnClearRadius)
        {
            this.terrain = terrain;
            this.seed = seed;
            this.chunkSize = chunkSize;
            spawnClearSqr = spawnClearRadius * spawnClearRadius;
            lakes = terrain.Lakes;
        }

        public void Generate(Vector2Int coord, ScatterProfile profile, List<ScatterPlacement> results)
        {
            results.Clear();
            accepted.Clear();

            ScatterProfile.Variant[] variants = profile.Variants;
            if (variants.Length == 0) return;

            // Filtro de variações (só com a ferramenta de calibração): tudo desmarcado = nenhum objeto deste perfil.
            bool filtering = ScatterTuning.HasFilter;
            if (filtering)
            {
                enabledVariants.Clear();
                if (enabledFlags.Length < variants.Length) enabledFlags = new bool[variants.Length];

                for (int v = 0; v < variants.Length; v++)
                {
                    bool on = ScatterTuning.IsEnabled(profile, in variants[v]);
                    enabledFlags[v] = on;
                    if (on) enabledVariants.Add(v);
                }

                if (enabledVariants.Count == 0) return;
            }

            bool grouped = profile.HasGroups;
            bool mix = grouped && profile.mixByPreference;
            bool useZones = profile.UsesZones;
            int zoneCount = profile.ZoneCount;
            if (grouped) BuildEligible(profile, variants, filtering, zoneCount, useZones);

            float originX = coord.x * chunkSize;
            float originZ = coord.y * chunkSize;
            float spacingSqr = profile.spacingByDensity ? 0f : profile.minSpacing * profile.minSpacing;
            float maxTerrainHeight = Mathf.Max(0.001f, terrain.MaxHeight);
            float minScale = Mathf.Min(profile.minScale, profile.maxScale);
            float maxScale = Mathf.Max(profile.minScale, profile.maxScale);
            float clusterFrequency = 1f / Mathf.Max(1f, profile.clusterSize);

            // Aplica as regras do terreno a uma posição já sorteada e, se passar, guarda o objeto.
            void TryPlace(float x, float z, float pick, int drawn, float yaw, float scale, float clusterRoll)
            {
                if (x * x + z * z < spawnClearSqr) return;

                float height = terrain.GetHeight(x, z);
                float normalizedHeight = height / maxTerrainHeight;
                if (normalizedHeight < profile.minHeight || normalizedHeight > profile.maxHeight) return;

                float slopeX = terrain.GetHeight(x + SlopeStep, z) - terrain.GetHeight(x - SlopeStep, z);
                float slopeZ = terrain.GetHeight(x, z + SlopeStep) - terrain.GetHeight(x, z - SlopeStep);
                Vector3 normal = new Vector3(-slopeX, 2f * SlopeStep, -slopeZ).normalized;
                float slope = Vector3.Angle(Vector3.up, normal);
                if (slope > profile.maxSlope) return;

                // Lagos: nada dentro da água; em volta, cada tipo de objeto tem a sua faixa (ver LakeMap.Allows).
                if (lakes != null && !lakes.Allows(profile.lakeBand, x, z, lakes.ShoreDistance(x, z))) return;

                if (profile.clustering > 0f)
                {
                    float cluster = terrain.GetCluster(x, z, clusterFrequency, profile.salt);
                    float chance = Mathf.Lerp(1f, Mathf.InverseLerp(ClusterMin, ClusterMax, cluster), profile.clustering);
                    if (clusterRoll > chance) return;
                }

                if (IsTooClose(x, z, spacingSqr)) return;
                accepted.Add(new Vector2(x, z));

                // Com grupos, o número sorteado escolhe primeiro o TIPO (pelo peso ou pela mistura por preferência) e depois a
                // variação dentro dele: cada tipo aparece com a mesma frequência, não importa quantas variações tenha.
                int variantIndex;
                if (grouped)
                {
                    variantIndex = PickGroupedVariant(profile, pick, mix, useZones ? ZoneAt(profile, x, z, zoneCount) : 0, x, z);
                    if (variantIndex < 0) return;
                }
                else
                {
                    variantIndex = filtering ? enabledVariants[(int)((long)drawn * enabledVariants.Count / variants.Length)] : drawn;
                }

                Quaternion tilt = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(Vector3.up, normal), profile.alignToSurface);

                // Altura final = terreno - afundamento fixo do perfil + altura do tipo (metros, absoluto; negativo enterra).
                int group = variants[variantIndex].group;

                results.Add(new ScatterPlacement
                {
                    VariantIndex = variantIndex,
                    Position = new Vector3(x, height - profile.sinkDepth * scale + ScatterTuning.GetElevation(profile, group), z),
                    Rotation = tilt * Quaternion.Euler(0f, yaw, 0f),
                    Scale = scale,
                    ColliderScale = ScatterTuning.GetColliderScale(profile, group)
                });
            }

            if (profile.spacingByDensity)
            {
                // GRADE: um objeto por célula de uma grade quadrada do mundo todo (cada um com 4 vizinhos à mesma distância),
                // com um pequeno desvio aleatório. O lado da célula é a raiz da área por objeto desejado: mais densidade =
                // células menores = objetos mais perto; menos densidade = mais longe. Como a grade é do mundo (não do chunk),
                // o padrão continua igual na divisa entre chunks. Cada célula tem o próprio sorteio (função de seed + célula).
                float target = profile.attemptsPerChunk * PerformanceSettings.GetDensity(profile.categoryId);
                if (target <= 0f) return;

                float cell = Mathf.Max(MinGridCell, chunkSize / Mathf.Sqrt(target));
                float jitter = Mathf.Clamp(profile.spacingJitter, 0f, MaxGridJitter);

                // A qualidade automática NÃO muda a grade (os objetos não andariam de lugar): tira uma fração das células ao acaso.
                float keep = PerformanceSettings.AutoQualityFactor;

                int firstX = Mathf.FloorToInt(originX / cell);
                int lastX = Mathf.FloorToInt((originX + chunkSize) / cell);
                int firstZ = Mathf.FloorToInt(originZ / cell);
                int lastZ = Mathf.FloorToInt((originZ + chunkSize) / cell);

                for (int cz = firstZ; cz <= lastZ; cz++)
                {
                    for (int cx = firstX; cx <= lastX; cx++)
                    {
                        Random cellRandom = new Random(ChunkSeed(new Vector2Int(cx, cz), profile));

                        // O desvio é menor que meia célula: o ponto fica dentro da própria célula.
                        float x = (cx + 0.5f + (cellRandom.NextFloat() * 2f - 1f) * jitter) * cell;
                        float z = (cz + 0.5f + (cellRandom.NextFloat() * 2f - 1f) * jitter) * cell;

                        float pick = 0f;
                        int drawn = 0;
                        if (grouped) pick = cellRandom.NextFloat();
                        else drawn = cellRandom.NextInt(variants.Length);

                        float yaw = cellRandom.NextFloat(360f);
                        float scale = cellRandom.NextFloat(minScale, maxScale);
                        float clusterRoll = cellRandom.NextFloat();
                        float thin = cellRandom.NextFloat();

                        // Cada ponto pertence a um só chunk (o que o contém).
                        if (x < originX || x >= originX + chunkSize || z < originZ || z >= originZ + chunkSize) continue;
                        if (thin >= keep) continue;

                        TryPlace(x, z, pick, drawn, yaw, scale, clusterRoll);
                    }
                }

                return;
            }

            // SORTEIO LIVRE (perfis sem grade, ex.: pedras e arbustos): posições ao acaso, com distância mínima fixa dentro do chunk.
            Random random = new Random(ChunkSeed(coord, profile));

            // Densidade escolhida pelo jogador no menu de Desempenho. Reduzir só corta o fim da sequência de
            // tentativas: os objetos que ficam continuam exatamente nos mesmos lugares.
            float density = profile.essential ? 1f : PerformanceSettings.GetEffectiveDensity(profile.categoryId);
            int attempts = Mathf.RoundToInt(profile.attemptsPerChunk * density);

            for (int i = 0; i < attempts; i++)
            {
                // Sorteia tudo antes de rejeitar: cada tentativa gasta sempre os mesmos números, então mudar
                // uma regra (ex.: inclinação máxima) não embaralha as posições das outras tentativas.
                float x = originX + random.NextFloat() * chunkSize;
                float z = originZ + random.NextFloat() * chunkSize;

                float pick = 0f;
                int drawn = 0;
                if (grouped) pick = random.NextFloat();
                else drawn = random.NextInt(variants.Length);

                float yaw = random.NextFloat(360f);
                float scale = random.NextFloat(minScale, maxScale);
                float clusterRoll = random.NextFloat();

                TryPlace(x, z, pick, drawn, yaw, scale, clusterRoll);
            }
        }

        private uint ChunkSeed(Vector2Int coord, ScatterProfile profile)
        {
            return math.max(1u, math.hash(new int4(seed, coord.x, coord.y, profile.salt)));
        }

        /// <summary>
        /// Monta, para cada zona e cada tipo, a lista de variações que podem nascer (do pacote da zona e, com a ferramenta de
        /// calibração, só as marcadas) e, por zona, a soma dos pesos dos tipos que têm alguma variação em cada preferência:
        /// a fatia da preferência é dividida entre esses tipos.
        /// </summary>
        private void BuildEligible(ScatterProfile profile, ScatterProfile.Variant[] variants, bool filtering, int zoneCount, bool useZones)
        {
            int groupCount = profile.groups.Length;

            if (eligible.Length < zoneCount)
            {
                System.Array.Resize(ref eligible, zoneCount);
                System.Array.Resize(ref tierWeightSum, zoneCount);
            }

            for (int z = 0; z < zoneCount; z++)
            {
                if (eligible[z] == null || eligible[z].Length < groupCount)
                {
                    eligible[z] = new List<int>[groupCount];
                    for (int g = 0; g < groupCount; g++) eligible[z][g] = new List<int>();
                }

                if (tierWeightSum[z] == null) tierWeightSum[z] = new float[4];
                System.Array.Clear(tierWeightSum[z], 0, tierWeightSum[z].Length);

                for (int g = 0; g < groupCount; g++)
                {
                    List<int> list = eligible[z][g];
                    list.Clear();

                    int first = profile.GetGroupFirst(g);
                    int length = profile.GetGroupLength(g);
                    for (int k = 0; k < length; k++)
                    {
                        int v = first + k;
                        if (filtering && !enabledFlags[v]) continue;

                        // Variação de um pacote de outra zona não nasce aqui (máscara -1 = pacote sem zona: nasce em todas).
                        if (useZones && (variants[v].zoneMask & (1 << z)) == 0) continue;

                        list.Add(v);
                    }

                    if (list.Count > 0) tierWeightSum[z][profile.GetPreference(g)] += profile.GetGroupWeight(g);
                }
            }
        }

        /// <summary>
        /// Zona do ponto (x, z) no mapa de zonas do perfil (<see cref="SeasonZoneMap"/>, o mesmo que o chão usa para misturar texturas).
        /// A troca de uma zona para outra é seca: não há árvores de duas zonas intercaladas.
        /// </summary>
        private int ZoneAt(ScatterProfile profile, float x, float z, int zoneCount)
        {
            return SeasonZoneMap.ZoneAt(seed, profile.ZoneSalt, profile.ZoneSize, zoneCount, x, z);
        }

        /// <summary>
        /// 0 = floresta baixa, 1 = floresta alta, em um ruído de variação lenta (manchas de <see cref="ScatterProfile.forestMixSize"/>
        /// metros). Depende só da posição e do perfil: o mesmo lugar sempre é do mesmo tipo de floresta.
        /// </summary>
        private float ForestHeight(ScatterProfile profile, float x, float z)
        {
            float noise = terrain.GetCluster(x, z, 1f / Mathf.Max(20f, profile.forestMixSize), profile.salt + ForestMixSaltOffset);
            float h = Mathf.Clamp01(0.5f + profile.forestMixBias * 0.5f + noise * profile.forestMixContrast * 0.5f);
            return h * h * (3f - 2f * h);
        }

        /// <summary>Calcula o peso de cada tipo neste ponto e devolve a soma. Sem mistura, vale o 'Weight' do tipo.</summary>
        private float FillGroupWeights(ScatterProfile profile, int zone, bool mix, float x, float z)
        {
            int groupCount = profile.groups.Length;
            if (groupWeights.Length < groupCount) groupWeights = new float[groupCount];

            Vector3 shares = mix ? Vector3.Lerp(profile.lowForestShares, profile.highForestShares, ForestHeight(profile, x, z)) : Vector3.zero;

            float total = 0f;
            for (int g = 0; g < groupCount; g++)
            {
                float weight = 0f;
                if (eligible[zone][g].Count > 0)
                {
                    weight = profile.GetGroupWeight(g);

                    if (mix)
                    {
                        int tier = profile.GetPreference(g);
                        float share = tier == 1 ? shares.x : (tier == 2 ? shares.y : shares.z);
                        weight = Mathf.Max(0f, share) * weight / tierWeightSum[zone][tier];
                    }
                }

                groupWeights[g] = weight;
                total += weight;
            }

            return total;
        }

        /// <summary>
        /// Escolhe o tipo (grupo) com probabilidade proporcional ao peso e, dentro dele, uma variação da zona. Usa um só
        /// número <paramref name="u"/> em [0,1): a parte que sobra depois de achar o tipo escolhe a variação. Com
        /// <paramref name="mix"/>, o peso de cada tipo vem da fatia da sua preferência no ponto (x, z). Devolve -1 se a zona
        /// não tem nenhuma variação liberada.
        /// </summary>
        private int PickGroupedVariant(ScatterProfile profile, float u, bool mix, int zone, float x, float z)
        {
            float total = FillGroupWeights(profile, zone, mix, x, z);

            // Todas as fatias liberadas valem zero (ex.: só sobrou um tipo cuja preferência tem fatia 0): volta aos pesos simples.
            if (total <= 0f && mix) total = FillGroupWeights(profile, zone, false, x, z);

            float target = u * total;
            float lower = 0f;
            int chosen = -1;
            float chosenLower = 0f;

            for (int g = 0; g < profile.groups.Length; g++)
            {
                if (groupWeights[g] <= 0f) continue;

                chosen = g;
                chosenLower = lower;
                lower += groupWeights[g];

                // O último tipo elegível recebe o resto: evita falha por arredondamento de float.
                if (target < lower) break;
            }

            if (chosen < 0) return -1;

            List<int> candidates = eligible[zone][chosen];
            float fraction = Mathf.Clamp01((target - chosenLower) / groupWeights[chosen]);
            int offset = Mathf.Min(candidates.Count - 1, (int)(fraction * candidates.Count));

            return candidates[offset];
        }

        private bool IsTooClose(float x, float z, float spacingSqr)
        {
            if (spacingSqr <= 0f) return false;

            for (int i = 0; i < accepted.Count; i++)
            {
                float dx = accepted[i].x - x;
                float dz = accepted[i].y - z;
                if (dx * dx + dz * dz < spacingSqr) return true;
            }

            return false;
        }
    }
}
