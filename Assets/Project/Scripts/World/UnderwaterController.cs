using UnityEngine;

namespace PROCEDURAL_GAME.World
{
    /// <summary>
    /// Efeito de estar debaixo d'água: quando a câmera passa da superfície de um lago para baixo, a cor da neblina, do ambiente e do
    /// fundo vira azul-esverdeada escura e a neblina fecha (a visão cai para 'Underwater Visibility', ~22 m), como de fato debaixo
    /// d'água. A mudança é suave (~0,25 s). Com a neblina fechada, a distância de desenho da câmera também encurta (o
    /// <see cref="ViewDistanceController"/> segue a densidade): debaixo d'água o jogo desenha bem menos coisa. Só existe em biomas com lagos.
    /// </summary>
    [RequireComponent(typeof(WorldGenerator))]
    // Antes do ViewDistanceController (-85), que usa a densidade daqui.
    [DefaultExecutionOrder(-86)]
    public class UnderwaterController : MonoBehaviour
    {
        private const float FadeSeconds = 0.25f;

        private WorldGenerator world;
        private LakeMap lakes;
        private BiomeProfile biome;
        private ViewDistanceController viewDistance;
        private Camera cam;
        private float factor;

        /// <summary>0 = fora d'água, 1 = totalmente debaixo d'água.</summary>
        public float Factor => factor;

        private void Awake()
        {
            world = GetComponent<WorldGenerator>();
        }

        private void Start()
        {
            lakes = world.Lakes;
            biome = world.Biome;
            viewDistance = GetComponent<ViewDistanceController>();

            if (lakes == null || biome == null) enabled = false;
        }

        private void Update()
        {
            if (cam == null)
            {
                cam = Camera.main;
                if (cam == null) return;
            }

            Vector3 position = cam.transform.position;
            bool under = lakes.Query(position.x, position.z, out float d, out float level, out _) && d < 0f && position.y < level;

            float previous = factor;
            factor = Mathf.MoveTowards(factor, under ? 1f : 0f, Time.unscaledDeltaTime / FadeSeconds);

            LakeSettings settings = lakes.Settings;
            if (viewDistance != null) viewDistance.SetUnderwater(factor, ViewDistance.FogDensityFor(settings.underwaterVisibility));

            // Fora d'água e sem ter acabado de sair: nada a fazer (o AtmosphereController cuida das cores).
            if (factor <= 0f && previous <= 0f) return;

            Color fog = Color.Lerp(biome.fogColor, settings.underwaterColor, factor);
            RenderSettings.fogColor = fog;
            RenderSettings.ambientLight = Color.Lerp(biome.ambientColor, settings.underwaterColor * 0.7f, factor);
            cam.backgroundColor = fog;
        }

        private void OnDisable()
        {
            if (viewDistance != null) viewDistance.SetUnderwater(0f, 0f);
        }
    }
}
