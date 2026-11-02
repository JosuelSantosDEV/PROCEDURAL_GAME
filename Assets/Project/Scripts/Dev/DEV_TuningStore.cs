#if UNITY_EDITOR || DEV_TOOLS
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PROCEDURAL_GAME.World;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PROCEDURAL_GAME.DevTools
{
    /// <summary>
    /// FERRAMENTA DE DESENVOLVIMENTO (não vai para o jogo final). Guarda os ajustes de calibração por tipo de árvore:
    /// quais tipos/pacotes aparecem, altura e multiplicador do colisor. Os valores ficam em PlayerPrefs (por perfil e tipo) e
    /// entram na geração por <see cref="ScatterTuning.Source"/>. Para remover tudo: apague a pasta Scripts/Dev.
    /// </summary>
    public sealed class DEV_TuningStore : IScatterTuningSource
    {
        public sealed class GroupState
        {
            public bool enabled = true;
            public float elevation;
            public float colliderScale = 1f;
        }

        private sealed class ProfileState
        {
            public GroupState[] groups;
            public readonly Dictionary<string, bool> packs = new Dictionary<string, bool>();
        }

        public static readonly DEV_TuningStore Instance = new DEV_TuningStore();

        private readonly Dictionary<ScatterProfile, ProfileState> states = new Dictionary<ScatterProfile, ProfileState>();

        // Registra a ferramenta antes da primeira cena: o primeiro mundo gerado já usa os valores salvos.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void DEV_Register() => ScatterTuning.Source = Instance;

        // ---- Leitura (geração) ----

        bool IScatterTuningSource.IsEnabled(ScatterProfile profile, in ScatterProfile.Variant variant)
        {
            if (variant.group < 0 || !profile.HasGroups) return true;

            GroupState group = DEV_GetGroup(profile, variant.group);
            return (group == null || group.enabled) && DEV_IsPackEnabled(profile, variant.pack);
        }

        float IScatterTuningSource.GetElevation(ScatterProfile profile, int group)
        {
            GroupState state = DEV_GetGroup(profile, group);
            return state != null ? state.elevation : profile.GetElevation(group);
        }

        float IScatterTuningSource.GetColliderScale(ScatterProfile profile, int group)
        {
            GroupState state = DEV_GetGroup(profile, group);
            return state != null ? state.colliderScale : profile.GetColliderScale(group);
        }

        // ---- Estado ----

        public GroupState DEV_GetGroup(ScatterProfile profile, int group)
        {
            if (!profile.HasGroups) return null;

            ProfileState state = DEV_GetProfile(profile);
            return group >= 0 && group < state.groups.Length ? state.groups[group] : null;
        }

        public bool DEV_IsPackEnabled(ScatterProfile profile, string pack)
        {
            if (string.IsNullOrEmpty(pack)) return true;

            ProfileState state = DEV_GetProfile(profile);
            if (!state.packs.TryGetValue(pack, out bool enabled))
            {
                enabled = PlayerPrefs.GetInt(DEV_PackKey(profile, pack), 1) == 1;
                state.packs[pack] = enabled;
            }

            return enabled;
        }

        public void DEV_SetPackEnabled(ScatterProfile profile, string pack, bool enabled)
        {
            DEV_GetProfile(profile).packs[pack] = enabled;
            PlayerPrefs.SetInt(DEV_PackKey(profile, pack), enabled ? 1 : 0);
        }

        public void DEV_SetEnabled(ScatterProfile profile, int group, bool enabled)
        {
            GroupState state = DEV_GetGroup(profile, group);
            if (state == null) return;

            state.enabled = enabled;
            PlayerPrefs.SetInt(DEV_Key(profile, group, "on"), enabled ? 1 : 0);
        }

        public void DEV_SetElevation(ScatterProfile profile, int group, float meters)
        {
            GroupState state = DEV_GetGroup(profile, group);
            if (state == null) return;

            state.elevation = Mathf.Round(Mathf.Clamp(meters, -50f, 50f) * 100f) / 100f;
            PlayerPrefs.SetFloat(DEV_Key(profile, group, "elev"), state.elevation);
        }

        public void DEV_SetColliderScale(ScatterProfile profile, int group, float multiplier)
        {
            GroupState state = DEV_GetGroup(profile, group);
            if (state == null) return;

            state.colliderScale = Mathf.Round(Mathf.Clamp(multiplier, 0.1f, 20f) * 100f) / 100f;
            PlayerPrefs.SetFloat(DEV_Key(profile, group, "col"), state.colliderScale);
        }

        /// <summary>Apaga os ajustes do perfil: volta a valer o que está gravado no asset.</summary>
        public void DEV_Reset(ScatterProfile profile)
        {
            if (profile.HasGroups)
            {
                for (int g = 0; g < profile.groups.Length; g++)
                {
                    PlayerPrefs.DeleteKey(DEV_Key(profile, g, "on"));
                    PlayerPrefs.DeleteKey(DEV_Key(profile, g, "elev"));
                    PlayerPrefs.DeleteKey(DEV_Key(profile, g, "col"));
                }

                foreach (ScatterProfile.Variant variant in profile.Variants)
                {
                    if (!string.IsNullOrEmpty(variant.pack)) PlayerPrefs.DeleteKey(DEV_PackKey(profile, variant.pack));
                }
            }

            states.Remove(profile);
        }

        public void DEV_Save() => PlayerPrefs.Save();

        // ---- Exportar ----

        /// <summary>Texto com os valores atuais, para colar na conversa e virar o padrão do projeto.</summary>
        public string DEV_BuildReport(ScatterProfile profile)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine($"[{profile.name}]");

            for (int g = 0; g < profile.groups.Length; g++)
            {
                GroupState state = DEV_GetGroup(profile, g);
                if (state == null) continue;

                string id = profile.groups[g].id;
                string elevation = state.elevation.ToString("0.00", CultureInfo.InvariantCulture);
                string collider = state.colliderScale.ToString("0.00", CultureInfo.InvariantCulture);
                text.AppendLine($"{id}: elevation={elevation}, colliderScale={collider}");
            }

            return text.ToString();
        }

#if UNITY_EDITOR
        /// <summary>Só no Editor: grava altura e colisor atuais no asset do perfil, como novo padrão.</summary>
        public void DEV_WriteToProfile(ScatterProfile profile)
        {
            for (int g = 0; g < profile.groups.Length; g++)
            {
                GroupState state = DEV_GetGroup(profile, g);
                if (state == null) continue;

                profile.groups[g].elevation = state.elevation;
                profile.groups[g].colliderScale = state.colliderScale;
            }

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
        }
#endif

        // ---- Interno ----

        private ProfileState DEV_GetProfile(ScatterProfile profile)
        {
            if (states.TryGetValue(profile, out ProfileState state)) return state;

            state = new ProfileState { groups = new GroupState[profile.HasGroups ? profile.groups.Length : 0] };

            // O que não foi mexido vale o que está gravado no asset.
            for (int g = 0; g < state.groups.Length; g++)
            {
                state.groups[g] = new GroupState
                {
                    enabled = PlayerPrefs.GetInt(DEV_Key(profile, g, "on"), 1) == 1,
                    elevation = PlayerPrefs.GetFloat(DEV_Key(profile, g, "elev"), profile.groups[g].elevation),
                    colliderScale = PlayerPrefs.GetFloat(DEV_Key(profile, g, "col"), profile.GetColliderScale(g))
                };
            }

            states[profile] = state;
            return state;
        }

        // Chave pelo id do tipo (ASCII estável), não pelo índice: reordenar a lista não troca os valores.
        private static string DEV_Key(ScatterProfile profile, int group, string field) => $"dev.{profile.name}.{profile.groups[group].id}.{field}";

        private static string DEV_PackKey(ScatterProfile profile, string pack) => $"dev.{profile.name}.pack.{pack}";
    }
}
#endif
