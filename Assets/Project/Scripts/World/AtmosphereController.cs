using UnityEngine;
using UnityEngine.Rendering;

namespace PROCEDURAL_GAME.World
{
    /// <summary>
    /// Aplica a atmosfera do <see cref="BiomeProfile"/> do mundo: neblina, luz ambiente, luz direcional e cor do céu.
    /// Fica no mesmo objeto do <see cref="WorldGenerator"/>, de onde lê o perfil (uma fonte só para cor do terreno e atmosfera).
    /// </summary>
    [RequireComponent(typeof(WorldGenerator))]
    public class AtmosphereController : MonoBehaviour
    {
        [Tooltip("Luz direcional a ajustar. Se vazio, usa a 'Sun Source' da cena ou a primeira luz direcional.")]
        [SerializeField] private Light sun;

        [Tooltip("Câmera que recebe a cor do céu. Se vazio, usa a Main Camera.")]
        [SerializeField] private Camera targetCamera;

        private void Start()
        {
            BiomeProfile biome = GetComponent<WorldGenerator>().Biome;
            if (biome != null) Apply(biome);
        }

        public void Apply(BiomeProfile biome)
        {
            // Neblina exponencial ao quadrado: clara perto, fecha rápido no limite da distância.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = biome.fogColor;
            RenderSettings.fogDensity = biome.fogDensity;

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = biome.ambientColor;
            DynamicGI.UpdateEnvironment();

            Light light = FindSun();
            if (light != null)
            {
                light.color = biome.lightColor;
                light.intensity = biome.lightIntensity;
                // As sombras da luz são controladas pelo ShadowController (nível escolhido no menu Desempenho).
            }

            // Céu = cor da neblina: assim o terreno distante some no fundo sem linha de horizonte.
            Camera cam = targetCamera != null ? targetCamera : Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = biome.fogColor;
            }
        }

        private Light FindSun()
        {
            if (sun != null) return sun;
            if (RenderSettings.sun != null) return RenderSettings.sun;

            foreach (Light candidate in FindObjectsOfType<Light>())
            {
                if (candidate.type == LightType.Directional) return candidate;
            }

            return null;
        }
    }
}
