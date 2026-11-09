#if UNITY_EDITOR || DEV_TOOLS
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace PROCEDURAL_GAME.DevTools
{
    /// <summary>
    /// Diagnóstico do travamento ao voltar para o jogo depois de minimizar. Mede, a partir da volta: o maior quadro dos 10 s seguintes (o jogo
    /// parou de desenhar?), o primeiro toque que chega ao Input System (o aparelho entregou o toque?) e o primeiro que chega à interface
    /// (o EventSystem o processou?). Mostra na tela e no Console. Não muda nada no jogo; sai junto com a pasta Dev.
    /// </summary>
    public class DEV_ResumeProbe : MonoBehaviour
    {
        private const float WatchSeconds = 10f;

        private float resumedAt = -1f;
        private float worstFrame;
        private float firstTouch;
        private float firstUiTouch;
        private bool finished;
        private string report;
        private GUIStyle style;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void DEV_Create()
        {
            GameObject probe = new GameObject(nameof(DEV_ResumeProbe));
            DontDestroyOnLoad(probe);
            probe.AddComponent<DEV_ResumeProbe>();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) return;

            resumedAt = Time.realtimeSinceStartup;
            worstFrame = 0f;
            firstTouch = -1f;
            firstUiTouch = -1f;
            finished = false;
            report = "Voltou: medindo... toque na tela";
        }

        private void Update()
        {
            if (resumedAt < 0f || finished) return;

            float elapsed = Time.realtimeSinceStartup - resumedAt;
            worstFrame = Mathf.Max(worstFrame, Time.unscaledDeltaTime);

            Touchscreen touchscreen = Touchscreen.current;
            if (touchscreen != null && touchscreen.primaryTouch.press.isPressed)
            {
                if (firstTouch < 0f) firstTouch = elapsed;

                int touchId = touchscreen.primaryTouch.touchId.ReadValue();
                if (firstUiTouch < 0f && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(touchId)) firstUiTouch = elapsed;
            }

            if (elapsed < WatchSeconds) return;

            finished = true;
            report = $"Volta: maior quadro {worstFrame * 1000f:0} ms | 1o toque {Describe(firstTouch)} | 1o toque na interface {Describe(firstUiTouch)}";
            Debug.Log($"[{nameof(DEV_ResumeProbe)}] {report}");
        }

        private static string Describe(float seconds) => seconds < 0f ? "nenhum" : $"{seconds:0.00} s";

        private void OnGUI()
        {
            if (string.IsNullOrEmpty(report)) return;

            if (style == null) style = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(14, Mathf.RoundToInt(Screen.height * 0.03f)), normal = { textColor = Color.yellow } };

            GUI.Label(new Rect(Screen.width * 0.05f, Screen.height * 0.9f, Screen.width * 0.9f, Screen.height * 0.1f), report, style);
        }
    }
}
#endif
