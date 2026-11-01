using PROCEDURAL_GAME.World;
using UnityEngine;

namespace PROCEDURAL_GAME.Core
{
    /// <summary>
    /// Leva escolhas do menu inicial para a cena do jogo (as cenas não compartilham objetos, mas campos estáticos sim).
    /// Se a cena do jogo for aberta direto (ex.: Play no Editor com GameWorld aberta), nada foi escolhido
    /// e o <see cref="WorldGenerator"/> usa o bioma configurado na própria cena.
    /// </summary>
    public static class GameSession
    {
        /// <summary>Bioma escolhido no menu; nulo = usar o da cena.</summary>
        public static BiomeProfile SelectedBiome { get; set; }

        // Com "Enter Play Mode Options" sem recarregar o domínio, campos estáticos sobrevivem entre Plays.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => SelectedBiome = null;
    }
}
