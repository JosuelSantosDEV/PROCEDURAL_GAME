using PROCEDURAL_GAME.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PROCEDURAL_GAME.World
{
    /// <summary>
    /// Aplica o nível de sombras (menu Desempenho > Sombras, reduzido pela qualidade automática) no jogo:
    /// - luz direcional: desligada, dura (Baixa) ou suave (Média e Alta);
    /// - URP: distância das sombras, definida no bioma (quanto menor, mais nítidas e mais baratas), e uma cascata só;
    /// - objetos espalhados: cada um projeta sombra a partir do nível definido no seu ScatterProfile.
    /// O terreno só recebe sombras (não projeta). A resolução do mapa de sombras (1024) e a qualidade do filtro suave (Low, 4 amostras) vêm do asset do URP: no URP 14 ela não
    /// muda em tempo de execução, então os níveis variam a distância.
    /// Nasce sozinho junto com o <see cref="WorldGenerator"/>; confere o nível todo frame (comparar dois inteiros).
    /// </summary>
    [RequireComponent(typeof(WorldGenerator))]
    public class ShadowController : MonoBehaviour
    {
        private WorldGenerator world;
        private UniversalRenderPipelineAsset urp;
        private Light sun;
        private float originalDistance;
        private int originalCascades;
        private int appliedLevel = -1;
        private bool warned;

        private void Awake()
        {
            world = GetComponent<WorldGenerator>();
            urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

            if (urp != null)
            {
                originalDistance = urp.shadowDistance;
                originalCascades = urp.shadowCascadeCount;
            }
        }

        private void Update()
        {
            int level = PerformanceSettings.EffectiveShadowLevel;
            if (level != appliedLevel) Apply(level);
        }

        private void OnDestroy()
        {
            // No Editor, mudar o asset do URP em jogo o alteraria de verdade: devolver o que estava.
            if (urp == null) return;

            urp.shadowDistance = originalDistance;
            urp.shadowCascadeCount = originalCascades;
        }

        private void Apply(int level)
        {
            appliedLevel = level;

            if (sun == null) sun = FindSun();
            if (sun != null) sun.shadows = level <= 0 ? LightShadows.None : level == PerformanceSettings.ShadowLow ? LightShadows.Hard : LightShadows.Soft;

            if (urp != null)
            {
                if (level > 0)
                {
                    // A distância vem do bioma (Biome Profile > Sombras): neblina mais densa pede sombras mais curtas.
                    urp.shadowDistance = world.Biome != null ? world.Biome.GetShadowDistance(level) : 25f;
                    urp.shadowCascadeCount = 1;
                }

                if (level > 0 && !urp.supportsMainLightShadows && !warned)
                {
                    warned = true;
                    Debug.LogWarning($"{nameof(ShadowController)}: o URP em uso ('{urp.name}') está com 'Main Light > Cast Shadows' desligado; não haverá sombras.", this);
                }
            }

            world.ApplyShadowLevel(level);
        }

        private static Light FindSun()
        {
            if (RenderSettings.sun != null) return RenderSettings.sun;

            foreach (Light candidate in FindObjectsOfType<Light>())
            {
                if (candidate.type == LightType.Directional) return candidate;
            }

            return null;
        }
    }
}
