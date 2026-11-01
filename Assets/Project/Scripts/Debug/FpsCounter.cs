using PROCEDURAL_GAME.Core;
using UnityEngine;

namespace PROCEDURAL_GAME.DebugTools
{
    /// <summary>
    /// Mostra FPS, tempo médio por frame e o pior frame do último intervalo, no canto superior esquerdo.
    /// O "pior frame" é o que revela travadas (ex.: criação de chunk) que a média esconde.
    /// Ferramenta de desenvolvimento: desmarque <c>Visible</c> (ou remova o componente) antes de publicar.
    /// </summary>
    public class FpsCounter : MonoBehaviour
    {
        [SerializeField] private bool visible = true;

        [Tooltip("De quanto em quanto tempo o número é atualizado, em segundos.")]
        [SerializeField, Min(0.1f)] private float refreshInterval = 0.5f;

        private float elapsed;
        private int frames;
        private float worstFrame;
        private string label = "...";
        private GUIStyle style;

        private void Awake()
        {
            // A escolha do jogador no menu de Desempenho vale mais que o valor marcado na cena.
            visible = PerformanceSettings.GetShowFpsCounter(visible);
        }

        private void Update()
        {
            // unscaledDeltaTime: continua correto mesmo se algum dia o jogo usar câmera lenta (Time.timeScale).
            float frameTime = Time.unscaledDeltaTime;
            elapsed += frameTime;
            frames++;
            worstFrame = Mathf.Max(worstFrame, frameTime);

            if (elapsed < refreshInterval) return;

            float averageMs = 1000f * elapsed / frames;
            label = $"{frames / elapsed:0} FPS\n{averageMs:0.0} ms  (pior {worstFrame * 1000f:0} ms)";

            elapsed = 0f;
            frames = 0;
            worstFrame = 0f;
        }

        // IMGUI cria um pouco de lixo de memória por frame: aceitável numa ferramenta de debug, não no jogo final.
        private void OnGUI()
        {
            if (!visible) return;

            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.Max(14, Mathf.RoundToInt(Screen.height * 0.035f)),
                    fontStyle = FontStyle.Bold
                };
            }

            // Respeita o "notch" e as bordas arredondadas. Screen.safeArea tem a origem embaixo; o IMGUI, em cima.
            Rect safe = Screen.safeArea;
            float margin = Screen.height * 0.02f;
            Rect area = new Rect(safe.x + margin, Screen.height - safe.yMax + margin, Screen.width * 0.6f, Screen.height * 0.2f);

            // Duas passadas (sombra preta + texto branco) para ler sobre qualquer fundo.
            style.normal.textColor = Color.black;
            GUI.Label(new Rect(area.x + 2f, area.y + 2f, area.width, area.height), label, style);
            style.normal.textColor = Color.white;
            GUI.Label(area, label, style);
        }
    }
}
