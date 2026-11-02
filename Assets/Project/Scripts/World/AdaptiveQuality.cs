using PROCEDURAL_GAME.Core;
using PROCEDURAL_GAME.DebugTools;
using UnityEngine;

namespace PROCEDURAL_GAME.World
{
    /// <summary>
    /// Qualidade automática: mede o FPS e, se não alcança o FPS desejado, reduz o raio de visão e a quantidade de objetos;
    /// se sobra folga, devolve qualidade aos poucos. Só age se estiver ligada no menu (Desempenho > Qualidade automática).
    ///
    /// Níveis (0 = qualidade máxima, a escolhida pelo jogador): os primeiros passos para baixo tiram um nível de sombras cada
    /// (Alta, Média, Baixa, desligadas: é o corte que menos muda o jogo e um dos mais caros). Depois, cada passo tira uma fração
    /// dos objetos de TODAS as categorias (inclusive as futuras) e, nos primeiros passos, também 1 chunk de raio, até o raio mínimo.
    /// Os sliders de objetos do menu continuam valendo como teto.
    ///
    /// Raio menor deixaria o fim do mundo à mostra; por isso a neblina engrossa na mesma medida (e antes do raio diminuir)
    /// e o raio nunca passa de <see cref="minViewRadius"/>.
    /// </summary>
    [RequireComponent(typeof(WorldGenerator))]
    // Depois do WorldGenerator (-100), que define o bioma, e antes de qualquer Start: os primeiros chunks já nascem no nível certo.
    [DefaultExecutionOrder(-90)]
    public class AdaptiveQuality : MonoBehaviour
    {
        [Header("Limites")]
        [Tooltip("Menor raio de visão permitido, em chunks. Abaixo de 2 o jogador veria o fim do mundo e a colisão não cobriria o entorno.")]
        [SerializeField, Min(2)] private int minViewRadius = 2;

        [Tooltip("Menor fração de objetos mantida (0,25 = nunca menos que 25% do que o jogador escolheu).")]
        [SerializeField, Range(0.05f, 0.9f)] private float minObjectFactor = 0.25f;

        [Tooltip("Quanto cada nível tira da quantidade de objetos.")]
        [SerializeField, Range(0.05f, 0.5f)] private float factorStep = 0.2f;

        [Header("Medição")]
        [Tooltip("Segundos iniciais sem medir: o carregamento dos primeiros chunks derruba o FPS e não diz nada sobre o aparelho.")]
        [SerializeField, Min(1f)] private float warmupSeconds = 8f;

        [Tooltip("Duração de cada medição de FPS.")]
        [SerializeField, Min(1f)] private float windowSeconds = 3f;

        [Tooltip("Segundos sem medir depois de cada mudança: os chunks levam um tempo para se refazer.")]
        [SerializeField, Min(1f)] private float settleSeconds = 6f;

        [Tooltip("Abaixo dessa fração do FPS desejado o jogo reduz a qualidade.")]
        [SerializeField, Range(0.5f, 1f)] private float lowerBelow = 0.90f;

        [Tooltip("A partir dessa fração do FPS desejado o jogo pode devolver qualidade. Precisa ser maior que o limite de reduzir (evita ficar oscilando).")]
        [SerializeField, Range(0.5f, 1.2f)] private float raiseAbove = 0.97f;

        [Tooltip("Espera mínima, em segundos, antes de tentar subir um nível.")]
        [SerializeField, Min(5f)] private float raiseDelay = 25f;

        [Tooltip("Se a qualidade devolvida não se sustenta, a espera da próxima tentativa dobra até esse máximo.")]
        [SerializeField, Min(5f)] private float maxRaiseDelay = 240f;

        [Header("Neblina")]
        [Tooltip("Neblina mínima para esconder a borda do mundo: densidade ≥ este valor ÷ (raio × tamanho do chunk). 1,9 esconde ~97% da borda.")]
        [SerializeField, Min(1f)] private float edgeFogFactor = 1.9f;

        [Tooltip("Velocidade da mudança de densidade da neblina (por segundo). Mudar devagar evita um 'pulo' visível.")]
        [SerializeField, Min(0.0005f)] private float fogChangePerSecond = 0.004f;

        private WorldGenerator world;
        private float targetFps;
        private float baseFog;
        private int baseRadius;
        private int minRadius;
        private int maxLevel;
        private int level;

        // Quantos níveis iniciais só reduzem as sombras (= o nível de sombras escolhido pelo jogador: 0 a 3).
        private int shadowLevels;

        private int appliedRadius;
        private int wantedRadius;
        private bool fogInitialized;

        private float elapsed;
        private float lastChangeAt;
        private float nextRaiseAt;
        private float currentRaiseDelay;
        private bool probing;
        private float windowTime;
        private int windowFrames;

        private void Awake()
        {
            world = GetComponent<WorldGenerator>();

            if (!PerformanceSettings.GetAutoQuality() || world.Biome == null)
            {
                enabled = false;
                return;
            }

            // Não passar do que a tela mostra: com 60 Hz, pedir 90 FPS nunca seria alcançado e a qualidade cairia à toa.
            float refresh = (float)Screen.currentResolution.refreshRateRatio.value;
            targetFps = Mathf.Min(PerformanceSettings.GetAutoTargetFps(), refresh > 0f ? refresh : float.MaxValue);

            baseFog = world.Biome.fogDensity;
            baseRadius = world.BaseViewRadius;
            minRadius = Mathf.Min(baseRadius, Mathf.Max(2, minViewRadius));

            // Pelo menos o necessário para o raio chegar ao mínimo, e o suficiente para os objetos chegarem ao fator mínimo,
            // mais os níveis que só tiram sombras.
            shadowLevels = PerformanceSettings.GetShadowLevel();
            int objectLevels = Mathf.CeilToInt((1f - minObjectFactor) / factorStep);
            int radiusLevels = Mathf.Max(0, 2 * (baseRadius - minRadius) - 1);
            maxLevel = shadowLevels + Mathf.Max(objectLevels, radiusLevels);

            level = Mathf.Clamp(PerformanceSettings.GetSavedAutoLevel(world.Biome.name), 0, maxLevel);
            currentRaiseDelay = raiseDelay;
            nextRaiseAt = raiseDelay;

            // O primeiro nível já vale na criação dos primeiros chunks (Start do WorldGenerator).
            PerformanceSettings.AutoQualityFactor = FactorFor(level);
            PerformanceSettings.AutoShadowReduction = ShadowCutFor(level);
            appliedRadius = RadiusFor(level);
            wantedRadius = appliedRadius;
            world.SetViewRadiusOverride(appliedRadius);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

            // A Atmosphere (Start) grava a neblina do bioma; só depois dela ajustamos, para a neblina já nascer certa.
            if (!fogInitialized)
            {
                RenderSettings.fogDensity = FogFor(appliedRadius);
                fogInitialized = true;
            }

            UpdateRadiusAndFog(dt);
            Measure(dt);

            FpsCounter.ExtraInfo = $"Auto: nível {level}/{maxLevel}  sombras {PerformanceSettings.EffectiveShadowLevel}  raio {appliedRadius}  objetos {PerformanceSettings.AutoQualityFactor * 100f:0}%";
        }

        private void OnDestroy()
        {
            // O fator é estático: sem isto o menu ou o próximo jogo herdariam o corte.
            PerformanceSettings.AutoQualityFactor = 1f;
            PerformanceSettings.AutoShadowReduction = 0;
            FpsCounter.ExtraInfo = null;
        }

        // Níveis que sobram depois dos que só tiram sombra: os de objetos e raio contam a partir daí.
        private int WorldLevel(int lvl) => Mathf.Max(0, lvl - shadowLevels);

        private int ShadowCutFor(int lvl) => Mathf.Min(lvl, shadowLevels);

        private float FactorFor(int lvl) => Mathf.Max(minObjectFactor, 1f - factorStep * WorldLevel(lvl));

        // O raio cai primeiro: 1 chunk no primeiro nível de mundo, outro no terceiro, etc., até o mínimo.
        private int RadiusFor(int lvl) => baseRadius - Mathf.Min(baseRadius - minRadius, (WorldLevel(lvl) + 1) / 2);

        private float FogFor(int radius)
        {
            if (radius >= baseRadius) return baseFog;
            return Mathf.Max(baseFog, edgeFogFactor / (radius * world.ChunkSize));
        }

        /// <summary>
        /// Raio maior vale na hora (a neblina fina volta aos poucos). Raio menor espera a neblina engrossar:
        /// do contrário, por alguns segundos o jogador veria a borda do mundo.
        /// </summary>
        private void UpdateRadiusAndFog(float dt)
        {
            if (wantedRadius > appliedRadius)
            {
                appliedRadius = wantedRadius;
                world.SetViewRadiusOverride(appliedRadius);
            }

            float fogTarget = FogFor(Mathf.Min(appliedRadius, wantedRadius));
            float fog = Mathf.MoveTowards(RenderSettings.fogDensity, fogTarget, fogChangePerSecond * dt);
            RenderSettings.fogDensity = fog;

            if (wantedRadius < appliedRadius && fog >= fogTarget * 0.98f)
            {
                appliedRadius = wantedRadius;
                world.SetViewRadiusOverride(appliedRadius);
            }
        }

        private void Measure(float dt)
        {
            elapsed += dt;

            // Pausa, troca de app e carregamento de cena geram quadros enormes que não representam o desempenho do jogo.
            bool ignoredFrame = Time.timeScale == 0f || dt > 0.5f;
            bool settling = elapsed < warmupSeconds || elapsed - lastChangeAt < settleSeconds;

            if (ignoredFrame || settling)
            {
                windowTime = 0f;
                windowFrames = 0;
                return;
            }

            windowTime += dt;
            windowFrames++;
            if (windowTime < windowSeconds) return;

            float fps = windowFrames / windowTime;
            windowTime = 0f;
            windowFrames = 0;

            Evaluate(fps / targetFps);
        }

        private void Evaluate(float ratio)
        {
            if (ratio < lowerBelow)
            {
                if (level >= maxLevel) return;

                // A subida anterior não se sustentou: esperar mais antes de tentar de novo.
                if (probing) currentRaiseDelay = Mathf.Min(currentRaiseDelay * 2f, maxRaiseDelay);
                probing = false;

                // Muito abaixo do alvo: descer dois níveis de uma vez em vez de esperar duas medições.
                int drop = ratio < 0.6f ? 2 : 1;
                SetLevel(Mathf.Min(maxLevel, level + drop));
                nextRaiseAt = elapsed + currentRaiseDelay;
                return;
            }

            // Primeira medição boa depois de subir: a qualidade devolvida se sustentou.
            if (probing)
            {
                probing = false;
                currentRaiseDelay = raiseDelay;
            }

            if (ratio >= raiseAbove && level > 0 && elapsed >= nextRaiseAt)
            {
                SetLevel(level - 1);
                probing = true;
                nextRaiseAt = elapsed + currentRaiseDelay;
            }
        }

        private void SetLevel(int newLevel)
        {
            bool factorChanged = !Mathf.Approximately(FactorFor(newLevel), FactorFor(level));

            level = newLevel;
            lastChangeAt = elapsed;
            // Gravar já: no Android o app pode ser fechado sem passar pelo ponto em que o PlayerPrefs é salvo.
            PerformanceSettings.SetSavedAutoLevel(world.Biome.name, level);
            PerformanceSettings.Save();

            wantedRadius = RadiusFor(level);

            // As sombras mudam na hora (o ShadowController confere todo frame); não há o que refazer nos chunks.
            PerformanceSettings.AutoShadowReduction = ShadowCutFor(level);

            if (factorChanged)
            {
                PerformanceSettings.AutoQualityFactor = FactorFor(level);
                world.RepopulateAll();
            }
        }
    }
}
