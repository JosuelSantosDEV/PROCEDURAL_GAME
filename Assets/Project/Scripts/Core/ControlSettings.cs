using UnityEngine;

namespace PROCEDURAL_GAME.Core
{
    /// <summary>
    /// Opções de controle escolhidas pelo jogador, gravadas no aparelho (PlayerPrefs): sensibilidade da câmera ao arrastar o dedo e
    /// transparência dos controles de toque. Os padrões (100% e 0%) mantêm o jogo como ele é sem ajuste nenhum.
    /// Os menus trabalham em passos inteiros de 5%, para o slider e o valor gravado baterem exatamente.
    /// </summary>
    public static class ControlSettings
    {
        private const int PercentStep = 5;
        private const int MinLookPercent = 25;
        private const int MaxLookPercent = 200;
        private const int DefaultLookPercent = 100;
        private const int MaxTransparencyPercent = 80;
        private const int DefaultTransparencyPercent = 0;

        public const int MaxLookSteps = (MaxLookPercent - MinLookPercent) / PercentStep;
        public const int MaxTransparencySteps = MaxTransparencyPercent / PercentStep;

        private const string LookKey = "controls.look";
        private const string TransparencyKey = "controls.transparency";

        // A câmera e os controles leem a cada frame; -1 = ainda não lido do PlayerPrefs.
        private static int lookCache = -1;
        private static int transparencyCache = -1;

        private static int LookPercent
        {
            get
            {
                if (lookCache < 0) lookCache = Mathf.Clamp(PlayerPrefs.GetInt(LookKey, DefaultLookPercent), MinLookPercent, MaxLookPercent);
                return lookCache;
            }
        }

        private static int TransparencyPercent
        {
            get
            {
                if (transparencyCache < 0) transparencyCache = Mathf.Clamp(PlayerPrefs.GetInt(TransparencyKey, DefaultTransparencyPercent), 0, MaxTransparencyPercent);
                return transparencyCache;
            }
        }

        /// <summary>Multiplicador da sensibilidade da câmera ao arrastar o dedo (1 = o valor do PlayerCameraController).</summary>
        public static float LookSensitivity => LookPercent / 100f;

        /// <summary>Opacidade dos controles de toque (1 = sem transparência adicional).</summary>
        public static float ControlsOpacity => 1f - TransparencyPercent / 100f;

        public static int GetLookSteps() => (LookPercent - MinLookPercent) / PercentStep;

        public static void SetLookSteps(int steps)
        {
            lookCache = Mathf.Clamp(MinLookPercent + steps * PercentStep, MinLookPercent, MaxLookPercent);
            PlayerPrefs.SetInt(LookKey, lookCache);
        }

        public static string FormatLookSteps(int steps) => $"{MinLookPercent + steps * PercentStep}%";

        public static int GetTransparencySteps() => TransparencyPercent / PercentStep;

        public static void SetTransparencySteps(int steps)
        {
            transparencyCache = Mathf.Clamp(steps * PercentStep, 0, MaxTransparencyPercent);
            PlayerPrefs.SetInt(TransparencyKey, transparencyCache);
        }

        public static string FormatTransparencySteps(int steps) => $"{steps * PercentStep}%";

        /// <summary>Apaga as escolhas: voltam a valer os padrões.</summary>
        public static void Reset()
        {
            PlayerPrefs.DeleteKey(LookKey);
            PlayerPrefs.DeleteKey(TransparencyKey);
            lookCache = -1;
            transparencyCache = -1;
        }

        /// <summary>Grava no disco (PlayerPrefs só persiste ao salvar ou fechar).</summary>
        public static void Save() => PlayerPrefs.Save();

        // Mantém o cache coerente quando o domínio não é recarregado entre Plays no Editor.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ClearCache()
        {
            lookCache = -1;
            transparencyCache = -1;
        }
    }
}
