using System;
using UnityEngine;

namespace PROCEDURAL_GAME.Core
{
    /// <summary>
    /// Define a taxa de quadros do jogo. Sem isso, o Android costuma rodar a 30 FPS por padrão,
    /// o que esconde o que o aparelho realmente aguenta (e deixa o jogo menos fluido do que poderia).
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class FrameRateController : MonoBehaviour
    {
        [Tooltip("FPS alvo. 0 = usar a taxa de atualização da tela (ex.: 120 em telas de 120 Hz). Taxas altas gastam mais bateria e esquentam o aparelho.")]
        [SerializeField, Min(0)] private int targetFrameRate = 60;

        [Tooltip("Impede a tela de apagar sozinha durante o jogo.")]
        [SerializeField] private bool keepScreenOn = true;

        private void Awake()
        {
            // O Unity ignora Application.targetFrameRate quando o VSync está ligado.
            QualitySettings.vSyncCount = 0;

            // A escolha do jogador no menu de Desempenho vale mais que o valor configurado na cena.
            // Com a qualidade automática ligada, o teto de FPS é o próprio FPS desejado: o jogo não gasta bateria acima disso.
            int fps = PerformanceSettings.GetAutoQuality()
                ? PerformanceSettings.GetAutoTargetFps()
                : PerformanceSettings.GetTargetFps(targetFrameRate);

            Application.targetFrameRate = fps > 0
                ? fps
                : Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value);

            if (keepScreenOn) Screen.sleepTimeout = SleepTimeout.NeverSleep;
        }
    }
}
