using PROCEDURAL_GAME.Core;
using PROCEDURAL_GAME.DebugTools;
using UnityEngine;

namespace PROCEDURAL_GAME.World
{
    /// <summary>
    /// Qualidade automática: mede o FPS e, se não alcança o FPS desejado, reduz a distância de visão e a quantidade de objetos;
    /// se sobra folga, devolve qualidade aos poucos. Só age se estiver ligada no menu (Desempenho > Qualidade automática).
    ///
    /// Níveis (0 = qualidade máxima, a escolhida pelo jogador): os primeiros passos para baixo tiram um nível de sombras cada
    /// (Alta, Média, Baixa, desligadas: é o corte que menos muda o jogo e um dos mais caros). Depois, cada passo tira uma fração
    /// dos objetos de TODAS as categorias (inclusive as futuras) e também uma fatia da distância de visão, até a distância mínima.
    /// Os sliders de objetos e a distância de visão do menu continuam valendo como teto.
    ///
    /// Distância menor deixaria o fim do mundo à mostra; por isso a neblina e o plano de corte da câmera acompanham (e a
    /// neblina engrossa antes do mundo descarregar): quem cuida disso é o <see cref="ViewDistanceController"/>.
    /// </summary>
    [RequireComponent(typeof(WorldGenerator))]
    // Depois do WorldGenerator (-100), que define o bioma, e antes de qualquer Start: os primeiros chunks já nascem no nível certo.
    [DefaultExecutionOrder(-90)]
    public class AdaptiveQuality : MonoBehaviour
    {
        [Header("Limites")]
        [Tooltip("Menor distância de visão permitida, em metros. Abaixo disso o jogador mal veria o entorno e a colisão não cobriria o que ele alcança.")]
        [SerializeField, Min(32f)] private float minViewDistance = 48f;

        [Tooltip("Quantos metros de distância de visão cada nível tira, até a distância mínima.")]
        [SerializeField, Min(4f)] private float distanceStep = 16f;

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

        private WorldGenerator world;
        private float targetFps;
        private ViewDistanceController viewDistance;
        private float baseDistance;
        private float minDistance;
        private int maxLevel;
        private int level;

        // Níveis iniciais que só reduzem a qualidade do solo (1 se o jogador a ligou, senão 0) e, depois, as sombras
        // (= o nível de sombras escolhido pelo jogador: 0 a 3).
        private int groundLevels;
        private int shadowLevels;

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

            viewDistance = GetComponent<ViewDistanceController>();
            baseDistance = PerformanceSettings.GetViewDistance();
            minDistance = Mathf.Min(baseDistance, minViewDistance);

            // Pelo menos o necessário para a distância chegar ao mínimo, e o suficiente para os objetos chegarem ao fator mínimo,
            // mais os níveis que só tiram sombras.
            groundLevels = PerformanceSettings.GetGroundQuality() ? 1 : 0;
            shadowLevels = PerformanceSettings.GetShadowLevel();
            int objectLevels = Mathf.CeilToInt((1f - minObjectFactor) / factorStep);
            int distanceLevels = Mathf.CeilToInt((baseDistance - minDistance) / distanceStep);
            maxLevel = groundLevels + shadowLevels + Mathf.Max(objectLevels, distanceLevels);

            level = Mathf.Clamp(PerformanceSettings.GetSavedAutoLevel(world.Biome.name), 0, maxLevel);
            currentRaiseDelay = raiseDelay;
            nextRaiseAt = raiseDelay;

            // O primeiro nível já vale na criação dos primeiros chunks (Start do WorldGenerator).
            PerformanceSettings.AutoQualityFactor = FactorFor(level);
            PerformanceSettings.AutoShadowReduction = ShadowCutFor(level);
            PerformanceSettings.AutoGroundCut = GroundCutFor(level);
            if (viewDistance != null) viewDistance.SetAutoCap(DistanceFor(level), immediate: true);
        }

        private void Update()
        {
            Measure(Time.unscaledDeltaTime);

            float shown = viewDistance != null ? viewDistance.AppliedDistance : baseDistance;
            FpsCounter.ExtraInfo = $"Auto: nível {level}/{maxLevel}  solo {(PerformanceSettings.EffectiveGroundQuality ? "on" : "off")}  sombras {PerformanceSettings.EffectiveShadowLevel}  visão {shown:0} m  objetos {PerformanceSettings.AutoQualityFactor * 100f:0}%";
        }

        private void OnDestroy()
        {
            // O fator é estático: sem isto o menu ou o próximo jogo herdariam o corte.
            PerformanceSettings.AutoQualityFactor = 1f;
            PerformanceSettings.AutoShadowReduction = 0;
            PerformanceSettings.AutoGroundCut = false;
            FpsCounter.ExtraInfo = null;
        }

        // Níveis que sobram depois dos que só tiram solo e sombra: os de objetos e distância contam a partir daí.
        private int WorldLevel(int lvl) => Mathf.Max(0, lvl - groundLevels - shadowLevels);

        // O primeiro corte é a qualidade do solo (o mapa de normal), depois as sombras, uma por nível.
        private bool GroundCutFor(int lvl) => groundLevels > 0 && lvl >= groundLevels;

        private int ShadowCutFor(int lvl) => Mathf.Clamp(lvl - groundLevels, 0, shadowLevels);

        private float FactorFor(int lvl) => Mathf.Max(minObjectFactor, 1f - factorStep * WorldLevel(lvl));

        // Cada nível de mundo tira um passo da distância de visão, até a mínima.
        private float DistanceFor(int lvl) => baseDistance - Mathf.Min(baseDistance - minDistance, distanceStep * WorldLevel(lvl));

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

            // O controlador engrossa a neblina antes de descarregar o mundo (distância menor) e afina depois (maior).
            if (viewDistance != null) viewDistance.SetAutoCap(DistanceFor(level));

            // As sombras mudam na hora (o ShadowController confere todo frame); não há o que refazer nos chunks.
            PerformanceSettings.AutoShadowReduction = ShadowCutFor(level);
            PerformanceSettings.AutoGroundCut = GroundCutFor(level);

            if (factorChanged)
            {
                PerformanceSettings.AutoQualityFactor = FactorFor(level);
                world.RepopulateAll();
            }
        }
    }
}
