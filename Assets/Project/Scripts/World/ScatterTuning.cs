namespace PROCEDURAL_GAME.World
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// <summary>
    /// Só existe no Editor e em Development Build: permite à ferramenta de calibração (pasta Scripts/Dev) trocar,
    /// sem mexer nos assets, quais tipos aparecem, a altura e o colisor de cada grupo.
    /// </summary>
    public interface IScatterTuningSource
    {
        bool IsEnabled(ScatterProfile profile, in ScatterProfile.Variant variant);
        float GetElevation(ScatterProfile profile, int group);
        float GetColliderScale(ScatterProfile profile, int group);
    }
#endif

    /// <summary>
    /// Ponto único por onde a geração lê os ajustes por tipo (altura e colisor). No jogo final, sem a ferramenta de
    /// desenvolvimento, tudo aqui se reduz a ler os valores gravados no perfil: nenhum código extra é compilado.
    /// </summary>
    public static class ScatterTuning
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>Preenchido pela ferramenta de calibração; nulo = valem os valores do perfil.</summary>
        public static IScatterTuningSource Source;
#endif

        /// <summary>Verdadeiro se alguma ferramenta pode esconder variações (a geração então filtra).</summary>
        public static bool HasFilter
        {
            get
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                return Source != null;
#else
                return false;
#endif
            }
        }

        public static bool IsEnabled(ScatterProfile profile, in ScatterProfile.Variant variant)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Source != null) return Source.IsEnabled(profile, in variant);
#endif
            return true;
        }

        public static float GetElevation(ScatterProfile profile, int group)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Source != null) return Source.GetElevation(profile, group);
#endif
            return profile.GetElevation(group);
        }

        public static float GetColliderScale(ScatterProfile profile, int group)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Source != null) return Source.GetColliderScale(profile, group);
#endif
            return profile.GetColliderScale(group);
        }
    }
}
