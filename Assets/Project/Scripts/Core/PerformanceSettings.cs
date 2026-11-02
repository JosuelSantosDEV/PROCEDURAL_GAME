using System.Collections.Generic;
using UnityEngine;

namespace PROCEDURAL_GAME.Core
{
    /// <summary>
    /// Opções de desempenho escolhidas pelo jogador, gravadas no aparelho (PlayerPrefs).
    /// Hoje: densidade por categoria de objeto (árvores, arbustos, rochas...), onde 1 = o valor do perfil,
    /// 0,5 = metade, 0 = nenhum. A categoria é identificada pelo campo 'Category Id' do <see cref="World.ScatterProfile"/>
    /// (ASCII, estável: é parte da chave gravada; o nome mostrado ao jogador é outro campo).
    /// Categorias novas aparecem no menu sozinhas e valem 1 até o jogador mexer.
    /// </summary>
    public static class PerformanceSettings
    {
        public const float DefaultDensity = 1f;
        public const float MinDensity = 0f;
        public const float MaxDensity = 2f;

        private const string DensityKeyPrefix = "density.";

        // PlayerPrefs é lido por chunk criado; o cache evita ir ao armazenamento toda vez.
        private static readonly Dictionary<string, float> densities = new Dictionary<string, float>();

        public static float GetDensity(string categoryId)
        {
            if (string.IsNullOrEmpty(categoryId)) return DefaultDensity;

            if (!densities.TryGetValue(categoryId, out float value))
            {
                value = PlayerPrefs.GetFloat(DensityKeyPrefix + categoryId, DefaultDensity);
                densities[categoryId] = value;
            }

            return value;
        }

        /// <summary>Densidade que vale agora na geração: a escolha do jogador × o corte da qualidade automática.</summary>
        public static float GetEffectiveDensity(string categoryId) => GetDensity(categoryId) * AutoQualityFactor;

        public static void SetDensity(string categoryId, float value)
        {
            if (string.IsNullOrEmpty(categoryId)) return;

            value = Mathf.Clamp(value, MinDensity, MaxDensity);
            densities[categoryId] = value;
            PlayerPrefs.SetFloat(DensityKeyPrefix + categoryId, value);
        }

        // FPS alvo: 0 gravado = jogador não escolheu, vale o valor do FrameRateController na cena.
        public const int MinTargetFps = 30;
        public const int MaxTargetFps = 120;
        public const int TargetFpsStep = 5;
        public const int DefaultTargetFps = 60;
        public const bool DefaultShowFpsCounter = true;

        private const string TargetFpsKey = "fps.target";
        private const string ShowFpsCounterKey = "fps.counter";

        /// <summary>FPS alvo escolhido pelo jogador, ou <paramref name="fallback"/> se ele nunca escolheu.</summary>
        public static int GetTargetFps(int fallback)
        {
            int saved = PlayerPrefs.GetInt(TargetFpsKey, 0);
            return saved > 0 ? saved : fallback;
        }

        public static void SetTargetFps(int fps) => PlayerPrefs.SetInt(TargetFpsKey, Mathf.Clamp(fps, MinTargetFps, MaxTargetFps));

        /// <summary>Se o contador de FPS aparece, ou <paramref name="fallback"/> se o jogador nunca escolheu.</summary>
        public static bool GetShowFpsCounter(bool fallback)
        {
            return PlayerPrefs.HasKey(ShowFpsCounterKey) ? PlayerPrefs.GetInt(ShowFpsCounterKey) == 1 : fallback;
        }

        public static void SetShowFpsCounter(bool show) => PlayerPrefs.SetInt(ShowFpsCounterKey, show ? 1 : 0);

        // Sombras em tempo real (luz direcional): 0 = desligadas; 1 = Baixa (só árvores, sombra dura, curta distância);
        // 2 = Média (árvores e rochas, sombra suave); 3 = Alta (maior distância). Cada objeto decide a partir de que nível
        // projeta sombra (campo 'Cast Shadows From Level' do ScatterProfile).
        public const int ShadowOff = 0;
        public const int ShadowLow = 1;
        public const int ShadowMedium = 2;
        public const int ShadowHigh = 3;
        public const int DefaultShadowLevel = ShadowLow;

        private const string ShadowLevelKey = "shadow.level";

        // -1 = ainda não lido do PlayerPrefs (é lido todo frame pelo ShadowController).
        private static int shadowLevelCache = -1;

        /// <summary>Nível de sombras escolhido pelo jogador (é o teto da qualidade automática).</summary>
        public static int GetShadowLevel()
        {
            if (shadowLevelCache < 0) shadowLevelCache = Mathf.Clamp(PlayerPrefs.GetInt(ShadowLevelKey, DefaultShadowLevel), ShadowOff, ShadowHigh);
            return shadowLevelCache;
        }

        public static void SetShadowLevel(int level)
        {
            shadowLevelCache = Mathf.Clamp(level, ShadowOff, ShadowHigh);
            PlayerPrefs.SetInt(ShadowLevelKey, shadowLevelCache);
        }

        /// <summary>Quantos níveis de sombra a qualidade automática tirou (as sombras são o primeiro item a reduzir). Não é gravado.</summary>
        public static int AutoShadowReduction { get; set; }

        /// <summary>Nível de sombras que vale agora: a escolha do jogador menos o corte da qualidade automática.</summary>
        public static int EffectiveShadowLevel => Mathf.Max(ShadowOff, GetShadowLevel() - AutoShadowReduction);

        // Qualidade automática: o jogo mede o FPS e ajusta raio de visão e quantidade de objetos para chegar no FPS desejado.
        public const int MinAutoFps = 15;
        public const int MaxAutoFps = 60;
        public const int AutoFpsStep = 5;
        public const int DefaultAutoFps = 30;

        private const string AutoQualityKey = "auto.enabled";
        private const string AutoFpsKey = "auto.fps";
        private const string AutoLevelKeyPrefix = "auto.level.";

        /// <summary>
        /// Fator de quantidade de objetos aplicado pela qualidade automática (1 = sem corte). Vale só durante o jogo
        /// e NÃO é gravado: os sliders do menu continuam mostrando a escolha do jogador, que é o teto.
        /// </summary>
        public static float AutoQualityFactor { get; set; } = 1f;

        public static bool GetAutoQuality() => PlayerPrefs.GetInt(AutoQualityKey, 0) == 1;

        public static void SetAutoQuality(bool enabled) => PlayerPrefs.SetInt(AutoQualityKey, enabled ? 1 : 0);

        public static int GetAutoTargetFps() => Mathf.Clamp(PlayerPrefs.GetInt(AutoFpsKey, DefaultAutoFps), MinAutoFps, MaxAutoFps);

        public static void SetAutoTargetFps(int fps) => PlayerPrefs.SetInt(AutoFpsKey, Mathf.Clamp(fps, MinAutoFps, MaxAutoFps));

        /// <summary>Último nível de qualidade que o aparelho aguentou nesse bioma: o jogo começa dele em vez de reaprender do zero.</summary>
        public static int GetSavedAutoLevel(string biomeName) => PlayerPrefs.GetInt(AutoLevelKeyPrefix + biomeName, 0);

        public static void SetSavedAutoLevel(string biomeName, int level) => PlayerPrefs.SetInt(AutoLevelKeyPrefix + biomeName, level);

        /// <summary>Apaga as escolhas gerais (FPS alvo, contador, qualidade automática e sombras): voltam a valer os valores da cena.</summary>
        public static void ResetGeneral()
        {
            PlayerPrefs.DeleteKey(TargetFpsKey);
            PlayerPrefs.DeleteKey(ShowFpsCounterKey);
            PlayerPrefs.DeleteKey(AutoQualityKey);
            PlayerPrefs.DeleteKey(AutoFpsKey);
            PlayerPrefs.DeleteKey(ShadowLevelKey);
            shadowLevelCache = -1;
        }

        /// <summary>Grava no disco. Chamar ao sair da tela de opções ou iniciar o jogo (PlayerPrefs só persiste ao salvar/fechar).</summary>
        public static void Save() => PlayerPrefs.Save();

        // Mantém o cache coerente quando o domínio não é recarregado entre Plays no Editor.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ClearCache()
        {
            densities.Clear();
            AutoQualityFactor = 1f;
            AutoShadowReduction = 0;
            shadowLevelCache = -1;
        }
    }
}
