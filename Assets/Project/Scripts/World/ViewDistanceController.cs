using PROCEDURAL_GAME.Core;
using UnityEngine;

namespace PROCEDURAL_GAME.World
{
    /// <summary>
    /// Mantém coerentes a distância de visão, a neblina e o plano de corte da câmera:
    /// - a distância vem do menu Desempenho (Perto, Médio, Longe), menos o corte da qualidade automática, se houver;
    /// - a neblina é a que cobre tudo nessa distância (<see cref="ViewDistance.FogDensityFor"/>);
    /// - a câmera só desenha até um pouco além disso (menos objetos desenhados = mais FPS).
    ///
    /// Distância menor espera a neblina engrossar antes de descarregar o mundo (senão o jogador veria a borda por alguns
    /// segundos); distância maior carrega na hora e a neblina afina aos poucos. Nasce junto com o <see cref="WorldGenerator"/>.
    /// </summary>
    [RequireComponent(typeof(WorldGenerator))]
    // Depois do WorldGenerator (-100) e da AdaptiveQuality (-90), antes de qualquer Start.
    [DefaultExecutionOrder(-85)]
    public class ViewDistanceController : MonoBehaviour
    {
        [Tooltip("Velocidade da mudança de densidade da neblina (por segundo). Mudar devagar evita um 'pulo' visível.")]
        [SerializeField, Min(0.0005f)] private float fogChangePerSecond = 0.004f;

        private WorldGenerator world;
        private Camera cam;
        private float originalFarClip;
        private float applied;
        private float autoCap = float.MaxValue;
        private bool fogInitialized;

        /// <summary>Distância (m) que o mundo está carregando agora.</summary>
        public float AppliedDistance => applied;

        /// <summary>
        /// Limite imposto pela qualidade automática, em metros (nunca passa da escolha do jogador). Use
        /// <see cref="float.MaxValue"/> para tirar o limite.
        /// </summary>
        /// <param name="immediate">Aplica já, sem esperar a neblina (só para o início do jogo, antes de qualquer chunk existir).</param>
        public void SetAutoCap(float meters, bool immediate = false)
        {
            autoCap = Mathf.Max(1f, meters);

            if (!immediate) return;

            applied = Wanted;
            world.SetViewDistance(applied);
        }

        private float Wanted => Mathf.Min(PerformanceSettings.GetViewDistance(), autoCap);

        private void Awake()
        {
            world = GetComponent<WorldGenerator>();
            applied = Wanted;
            world.SetViewDistance(applied);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            float wanted = Wanted;

            // Distância maior vale na hora; a neblina afina aos poucos, mais devagar que o mundo se carrega.
            if (wanted > applied)
            {
                applied = wanted;
                world.SetViewDistance(applied);
            }

            float fogTarget = ViewDistance.FogDensityFor(Mathf.Min(applied, wanted));
            float fog = fogInitialized ? Mathf.MoveTowards(RenderSettings.fogDensity, fogTarget, fogChangePerSecond * dt) : fogTarget;
            fogInitialized = true;
            RenderSettings.fogDensity = fog;

            // Distância menor: só descarrega quando a neblina já cobre a nova distância.
            if (wanted < applied && fog >= fogTarget * 0.98f)
            {
                applied = wanted;
                world.SetViewDistance(applied);
            }

            ApplyFarClip(fog);
        }

        // Plano de corte = onde a neblina atual cobre tudo (ou o que o mundo carrega, se menor) + folga.
        private void ApplyFarClip(float fogDensity)
        {
            if (cam == null)
            {
                cam = Camera.main;
                if (cam == null) return;
                originalFarClip = cam.farClipPlane;
            }

            float far = ViewDistance.FarClipFor(Mathf.Min(ViewDistance.CoverDistance(fogDensity), applied));
            far = Mathf.Max(far, cam.nearClipPlane + 1f);
            if (Mathf.Abs(cam.farClipPlane - far) > 0.25f) cam.farClipPlane = far;
        }

        private void OnDestroy()
        {
            // Deixa a câmera como estava (o plano de corte é da cena, não do jogo).
            if (cam != null && originalFarClip > 0f) cam.farClipPlane = originalFarClip;
        }
    }
}
