using System.Collections.Generic;
using UnityEngine;

namespace PROCEDURAL_GAME.Core
{
    /// <summary>
    /// Opções de desempenho escolhidas pelo jogador, gravadas no aparelho (PlayerPrefs).
    /// Hoje: densidade por categoria de objeto (Árvores, Arbustos, Rochas...), onde 1 = o valor do perfil,
    /// 0,5 = metade, 0 = nenhum. A categoria é o texto do campo 'Category' do <see cref="World.ScatterProfile"/>;
    /// categorias novas aparecem no menu sozinhas e valem 1 até o jogador mexer.
    /// </summary>
    public static class PerformanceSettings
    {
        public const float DefaultDensity = 1f;
        public const float MinDensity = 0f;
        public const float MaxDensity = 2f;

        private const string DensityKeyPrefix = "density.";

        // PlayerPrefs é lido por chunk criado; o cache evita ir ao armazenamento toda vez.
        private static readonly Dictionary<string, float> densities = new Dictionary<string, float>();

        public static float GetDensity(string category)
        {
            if (string.IsNullOrEmpty(category)) return DefaultDensity;

            if (!densities.TryGetValue(category, out float value))
            {
                value = PlayerPrefs.GetFloat(DensityKeyPrefix + category, DefaultDensity);
                densities[category] = value;
            }

            return value;
        }

        public static void SetDensity(string category, float value)
        {
            if (string.IsNullOrEmpty(category)) return;

            value = Mathf.Clamp(value, MinDensity, MaxDensity);
            densities[category] = value;
            PlayerPrefs.SetFloat(DensityKeyPrefix + category, value);
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

        /// <summary>Apaga as escolhas de FPS (alvo e contador): voltam a valer os valores da cena.</summary>
        public static void ResetGeneral()
        {
            PlayerPrefs.DeleteKey(TargetFpsKey);
            PlayerPrefs.DeleteKey(ShowFpsCounterKey);
        }

        /// <summary>Grava no disco. Chamar ao sair da tela de opções ou iniciar o jogo (PlayerPrefs só persiste ao salvar/fechar).</summary>
        public static void Save() => PlayerPrefs.Save();

        // Mantém o cache coerente quando o domínio não é recarregado entre Plays no Editor.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ClearCache() => densities.Clear();
    }
}
