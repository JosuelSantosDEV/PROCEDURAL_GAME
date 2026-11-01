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

            Application.targetFrameRate = targetFrameRate > 0
                ? targetFrameRate
                : Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value);

            if (keepScreenOn) Screen.sleepTimeout = SleepTimeout.NeverSleep;
        }
    }
}
