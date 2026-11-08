using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace PROCEDURAL_GAME.World
{
    /// <summary>
    /// Onde ficam os lagos de um mundo e como eles moldam o terreno. É função só de seed + posição (como o relevo): o mesmo lugar
    /// sempre tem o mesmo lago, então chunks se encaixam e podem ser descartados e recriados.
    /// O mundo é dividido em células de <see cref="LakeSettings.cellSize"/>; cada uma pode ter um lago, com o centro entre 30% e
    /// 70% da célula (um lago nunca atravessa a divisa de células) e contorno irregular (o raio varia com o ângulo).
    /// Distância da margem: <c>d = distância ao centro - raio(ângulo)</c>; negativa = dentro do lago.
    /// </summary>
    public sealed class LakeMap
    {
        /// <summary>Valor de distância para "nenhum lago por perto".</summary>
        public const float NoLake = 1e6f;

        // O raio do contorno vai de ~0,58 a ~1,42 do raio médio.
        private const float MaxShape = 1.42f;

        // Manchas de palmeiras e árvores: as bordas das manchas são tortas (o mundo é deformado por ruído antes de dividir em
        // quadrados) para não formarem corredores retos.
        private const float PatchWarpMeters = 7f;
        private const float PatchWarpFrequency = 0.028f;

        private sealed class Lake
        {
            public bool exists;
            public float centerX, centerZ, radius, level, phase1, phase2, phase3, reachSqr;
        }

        private readonly int seed;
        private readonly LakeSettings settings;
        private readonly Func<float, float, float> baseHeight;
        private readonly Dictionary<long, Lake> cache = new Dictionary<long, Lake>();
        private readonly Dictionary<long, bool> patchCache = new Dictionary<long, bool>();
        private readonly float2 warpOffsetA;
        private readonly float2 warpOffsetB;

        // Lagos das 3x3 células em volta da última posição consultada (consultas seguidas ficam quase sempre na mesma célula).
        private readonly Lake[] near = new Lake[9];
        private int nearCount;
        private int nearCellX = int.MinValue;
        private int nearCellZ = int.MinValue;

        public LakeSettings Settings => settings;

        /// <param name="baseHeight">Altura natural do relevo (sem lagos): define o nível da água de cada lago.</param>
        public LakeMap(int seed, LakeSettings settings, Func<float, float, float> baseHeight)
        {
            this.seed = seed;
            this.settings = settings;
            this.baseHeight = baseHeight;

            warpOffsetA = new float2(Unit(11, 3, 50) * 900f, Unit(12, 4, 51) * 900f);
            warpOffsetB = new float2(Unit(13, 5, 52) * 900f, Unit(14, 6, 53) * 900f);
        }

        /// <summary>
        /// Lago mais próximo do ponto, se houver um a menos de <see cref="LakeSettings.TotalReach"/> metros da margem.
        /// <paramref name="d"/> = distância da margem (negativa dentro), <paramref name="level"/> = nível da água, <paramref name="r"/> = distância
        /// ao centro dividida pelo raio naquela direção (menor que 1 dentro do lago).
        /// </summary>
        public bool Query(float x, float z, out float d, out float level, out float r)
        {
            EnsureNear(x, z);

            d = NoLake;
            level = 0f;
            r = 0f;
            bool found = false;

            for (int i = 0; i < nearCount; i++)
            {
                Lake lake = near[i];
                float dx = x - lake.centerX;
                float dz = z - lake.centerZ;
                float distanceSqr = dx * dx + dz * dz;
                if (distanceSqr > lake.reachSqr) continue;

                float distance = Mathf.Sqrt(distanceSqr);
                float shore = RadiusAt(lake, Mathf.Atan2(dz, dx));
                float candidate = distance - shore;
                if (candidate >= d) continue;

                d = candidate;
                level = lake.level;
                r = distance / shore;
                found = true;
            }

            return found;
        }

        /// <summary>
        /// Procura o lago mais próximo de (<paramref name="fromX"/>, <paramref name="fromZ"/>) num raio de <paramref name="maxCells"/> células
        /// e devolve um ponto a <paramref name="offsetMeters"/> da margem, do lado de quem procura (para ficar em cima do platô, olhando a água).
        /// <paramref name="centerDistance"/> = distância até o centro do lago.
        /// </summary>
        public bool TryFindNearest(float fromX, float fromZ, int maxCells, float offsetMeters, out Vector2 point, out float centerDistance)
        {
            point = Vector2.zero;
            centerDistance = NoLake;

            int cellX = Mathf.FloorToInt(fromX / settings.cellSize);
            int cellZ = Mathf.FloorToInt(fromZ / settings.cellSize);
            Lake best = null;

            for (int dz = -maxCells; dz <= maxCells; dz++)
            {
                for (int dx = -maxCells; dx <= maxCells; dx++)
                {
                    Lake lake = GetLake(cellX + dx, cellZ + dz);
                    if (!lake.exists) continue;

                    float distance = Mathf.Sqrt((lake.centerX - fromX) * (lake.centerX - fromX) + (lake.centerZ - fromZ) * (lake.centerZ - fromZ));
                    if (distance >= centerDistance) continue;

                    centerDistance = distance;
                    best = lake;
                }
            }

            if (best == null) return false;

            float angle = centerDistance > 0.01f ? Mathf.Atan2(fromZ - best.centerZ, fromX - best.centerX) : 0f;
            float reach = RadiusAt(best, angle) + offsetMeters;
            point = new Vector2(best.centerX + Mathf.Cos(angle) * reach, best.centerZ + Mathf.Sin(angle) * reach);
            return true;
        }

        /// <summary>Distância da margem (m): negativa dentro do lago; <see cref="NoLake"/> se não há lago por perto.</summary>
        public float ShoreDistance(float x, float z)
        {
            return Query(x, z, out float d, out _, out _) ? d : NoLake;
        }

        /// <summary>Altura do terreno com os lagos escavados, a partir da altura natural <paramref name="natural"/> do ponto.</summary>
        public float Carve(float x, float z, float natural)
        {
            if (!Query(x, z, out float d, out float level, out float r)) return natural;

            if (d < 0f)
            {
                // Dentro: fundo em tigela, na margem encosta no nível da água.
                return level - settings.maxDepth * (1f - r * r);
            }

            // O lago é uma DEPRESSÃO larga (ilustração depressao_desejada): o próprio terreno desce de fora até a água, sem
            // barranco. 'top' = altura de onde a descida parte: a do centro do lago ('levelOffset' acima da água) misturada à natural
            // (guarda um pouco do relevo local), nunca menos de 2 m acima da água.
            float width = Mathf.Max(8f, settings.depressionMeters);
            float top = Mathf.Max(level + 2f, Mathf.Lerp(level + settings.levelOffset, natural, 0.5f));

            if (d < width)
            {
                // Quase uma rampa constante, só arredondada no alto (70% de S): inclinação ~0,03 junto da água e ~0,13 no meio.
                float t = d / width;
                float profile = Mathf.Lerp(t, Mathf.SmoothStep(0f, 1f, t), 0.7f);
                return level + (top - level) * profile;
            }

            float blendEnd = settings.BlendEnd;
            if (d < blendEnd)
            {
                // Depois da depressão: volta devagar ao relevo natural.
                float blend = Mathf.SmoothStep(0f, 1f, (d - width) / (blendEnd - width));
                return Mathf.Lerp(top, natural, blend);
            }

            return natural;
        }

        /// <summary>
        /// Pesos de areia do chão no ponto: x = areia (1 até o fim da faixa só das palmeiras, depois some até o fim da mistura),
        /// y = quanto dessa areia é a do FUNDO do lago (abaixo da água; 0 na margem). <paramref name="height"/> é a altura do terreno no ponto.
        /// </summary>
        public Vector2 GroundWeights(float x, float z, float height)
        {
            if (!Query(x, z, out float d, out float level, out _)) return Vector2.zero;

            float sand = SandWeight(d);
            if (sand <= 0f) return Vector2.zero;

            // Na margem a areia é a mesma da praia; mais fundo vai ficando a areia lisa do fundo.
            float bed = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.3f, 1.2f, level - height));
            return new Vector2(sand, bed);
        }

        /// <summary>Peso da textura de areia a <paramref name="d"/> metros da margem: 1 até o fim da faixa só das palmeiras, depois some em 'Mix Meters'.</summary>
        public float SandWeight(float d)
        {
            if (d >= NoLake * 0.5f) return 0f;

            float start = settings.PalmOnlyEnd;
            if (d <= start) return 1f;

            return 1f - Mathf.SmoothStep(0f, 1f, (d - start) / Mathf.Max(0.01f, settings.mixMeters));
        }

        /// <summary>
        /// Se um objeto da faixa <paramref name="band"/> pode nascer em (<paramref name="x"/>, <paramref name="z"/>), a
        /// <paramref name="d"/> metros da margem. Regras, de dentro para fora:
        /// 0 a 'Bare Sand': nada. Até o fim de 'Palm Meters': só palmeiras e arbustos. Em 'Mix Meters' e 'Intro Meters': árvores,
        /// arbustos e palmeiras, as palmeiras e as árvores em manchas separadas (uma palmeira nunca fica junto de uma árvore),
        /// com mais palmeiras perto do lago e palmeiras raras já dentro da floresta (a faixa de entrada). Depois: floresta normal.
        /// </summary>
        public bool Allows(LakeBand band, float x, float z, float d)
        {
            // Sem lago por perto: tudo nasce normalmente, MENOS palmeiras (só existem junto da água).
            if (d >= NoLake * 0.5f) return band != LakeBand.Palms;

            switch (band)
            {
                case LakeBand.Palms:
                    if (d < settings.bareSandMeters || d >= settings.IntroEnd) return false;
                    return PatchAllows(true, x, z);

                case LakeBand.Shrubs:
                    return d >= settings.bareSandMeters;

                case LakeBand.NormalTrees:
                    if (d < settings.PalmOnlyEnd) return false;
                    // Um pouco além das palmeiras ainda vale a folga em relação às manchas delas.
                    if (d >= settings.IntroEnd + settings.patchMeters) return true;
                    return PatchAllows(false, x, z);

                default:
                    // Qualquer outro objeto só não nasce dentro da água.
                    return d >= 1f;
            }
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Manchas de palmeiras e de árvores
        // ---------------------------------------------------------------------------------------------------------------

        // O mundo (deformado por ruído) é dividido em quadrados de 'patchMeters'. Cada quadrado é de palmeiras ou de árvores,
        // sorteado com a chance de palmeira de PalmChance (100% junto do lago, 0% no fim da faixa de entrada). Um objeto só
        // nasce no quadrado do seu tipo e a pelo menos 'patchGapMeters' de qualquer quadrado do outro tipo: então palmeira e
        // árvore ficam sempre separadas por essa folga (no espaço deformado; a deformação é suave, o erro é pequeno).
        private bool PatchAllows(bool palm, float x, float z)
        {
            float size = Mathf.Max(8f, settings.patchMeters);
            Warp(x, z, out float wx, out float wz);

            int tileX = Mathf.FloorToInt(wx / size);
            int tileZ = Mathf.FloorToInt(wz / size);
            if (IsPalmTile(tileX, tileZ, size) != palm) return false;

            float gap = settings.patchGapMeters;
            if (gap <= 0f) return true;

            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    if (IsPalmTile(tileX + dx, tileZ + dz, size) == palm) continue;

                    float minX = (tileX + dx) * size;
                    float minZ = (tileZ + dz) * size;
                    float outX = Mathf.Max(minX - wx, 0f, wx - (minX + size));
                    float outZ = Mathf.Max(minZ - wz, 0f, wz - (minZ + size));
                    if (outX * outX + outZ * outZ < gap * gap) return false;
                }
            }

            return true;
        }

        // Chance de um quadrado a 'd' metros da margem ser de palmeiras: 100% até o fim da faixa só das palmeiras; cai até
        // 'introPalmShare' no fim da mistura; daí cai a 0% no fim da faixa de entrada (palmeiras raras DENTRO da floresta).
        private float PalmChance(float d)
        {
            float onlyEnd = settings.PalmOnlyEnd;
            if (d <= onlyEnd) return 1f;

            float share = settings.introPalmShare;
            float mixEnd = settings.MixEnd;
            if (d < mixEnd) return Mathf.Lerp(1f, share, Mathf.SmoothStep(0f, 1f, (d - onlyEnd) / Mathf.Max(0.01f, settings.mixMeters)));

            float introEnd = settings.IntroEnd;
            if (d < introEnd) return share * (1f - Mathf.SmoothStep(0f, 1f, (d - mixEnd) / Mathf.Max(0.01f, settings.introMeters)));

            return 0f;
        }

        private bool IsPalmTile(int tileX, int tileZ, float size)
        {
            long key = ((long)tileX << 32) ^ (uint)tileZ;
            if (patchCache.TryGetValue(key, out bool palm)) return palm;

            // Distância da margem no centro do quadrado (tratado como ponto do mundo, mesmo deformado).
            float d = ShoreDistance((tileX + 0.5f) * size, (tileZ + 0.5f) * size);
            float chance = d < NoLake * 0.5f ? PalmChance(d) : 0f;

            palm = Unit(tileX, tileZ, 20) < chance;
            patchCache[key] = palm;
            return palm;
        }

        private void Warp(float x, float z, out float wx, out float wz)
        {
            float2 p = new float2(x, z) * PatchWarpFrequency;
            wx = x + PatchWarpMeters * noise.snoise(p + warpOffsetA);
            wz = z + PatchWarpMeters * noise.snoise(p + warpOffsetB);
        }

        // ---------------------------------------------------------------------------------------------------------------

        private static float RadiusAt(Lake lake, float angle)
        {
            float shape = 1f
                + 0.22f * Mathf.Sin(2f * angle + lake.phase1)
                + 0.12f * Mathf.Sin(3f * angle + lake.phase2)
                + 0.08f * Mathf.Sin(5f * angle + lake.phase3);
            return lake.radius * shape;
        }

        private void EnsureNear(float x, float z)
        {
            int cellX = Mathf.FloorToInt(x / settings.cellSize);
            int cellZ = Mathf.FloorToInt(z / settings.cellSize);
            if (cellX == nearCellX && cellZ == nearCellZ) return;

            nearCellX = cellX;
            nearCellZ = cellZ;
            nearCount = 0;

            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    Lake lake = GetLake(cellX + dx, cellZ + dz);
                    if (lake.exists) near[nearCount++] = lake;
                }
            }
        }

        private Lake GetLake(int cellX, int cellZ)
        {
            long key = ((long)cellX << 32) ^ (uint)cellZ;
            if (cache.TryGetValue(key, out Lake lake)) return lake;

            lake = CreateLake(cellX, cellZ);
            cache[key] = lake;
            return lake;
        }

        private Lake CreateLake(int cellX, int cellZ)
        {
            Lake lake = new Lake();

            if (Unit(cellX, cellZ, 0) >= settings.chance) return lake;

            float cell = settings.cellSize;
            lake.centerX = (cellX + 0.3f + 0.4f * Unit(cellX, cellZ, 1)) * cell;
            lake.centerZ = (cellZ + 0.3f + 0.4f * Unit(cellX, cellZ, 2)) * cell;
            lake.radius = Mathf.Lerp(Mathf.Min(settings.radiusRange.x, settings.radiusRange.y), Mathf.Max(settings.radiusRange.x, settings.radiusRange.y), Unit(cellX, cellZ, 3));
            lake.phase1 = Unit(cellX, cellZ, 4) * Mathf.PI * 2f;
            lake.phase2 = Unit(cellX, cellZ, 5) * Mathf.PI * 2f;
            lake.phase3 = Unit(cellX, cellZ, 6) * Mathf.PI * 2f;

            // Nada de lago perto da origem: o Player nasce ali.
            float fromOrigin = Mathf.Sqrt(lake.centerX * lake.centerX + lake.centerZ * lake.centerZ);
            if (fromOrigin < settings.clearRadius + lake.radius * MaxShape) return lake;

            lake.level = baseHeight(lake.centerX, lake.centerZ) - settings.levelOffset;

            float reach = lake.radius * MaxShape + settings.TotalReach;
            lake.reachSqr = reach * reach;
            lake.exists = true;
            return lake;
        }

        // Número de 0 a 1 que só depende de seed + célula + índice.
        private float Unit(int cellX, int cellZ, int index)
        {
            uint h = math.hash(new int4(seed ^ 0x2F1B3C5, cellX, cellZ, index + 7919));
            return (h & 0xFFFFFFu) / 16777216f;
        }
    }
}
