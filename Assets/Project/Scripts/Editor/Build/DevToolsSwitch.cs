using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PROCEDURAL_GAME.EditorTools
{
    /// <summary>
    /// Liga e desliga as ferramentas de desenvolvimento (pasta Scripts/Dev: calibração das árvores) nos BUILDS, pelo símbolo de
    /// compilação DEV_TOOLS. No Editor elas existem sempre.
    /// - Ligado: o aparelho de teste mostra o botão DEV e aplica os ajustes salvos.
    /// - Desligado (versão comercial): o compilador nem inclui esse código; custo zero no jogo final.
    /// Menu: PROCEDURAL_GAME > Build > Dev Tools nos builds (marcado = ligado). Todo build avisa no Console qual é o caso.
    /// </summary>
    [InitializeOnLoad]
    public static class DevToolsSwitch
    {
        public const string Symbol = "DEV_TOOLS";

        private const string MenuPath = "PROCEDURAL_GAME/Build/Dev Tools nos builds";

        // Plataformas em que o jogo é compilado.
        private static readonly NamedBuildTarget[] Targets = { NamedBuildTarget.Android, NamedBuildTarget.iOS, NamedBuildTarget.Standalone };

        static DevToolsSwitch()
        {
            // Padrão do projeto durante o desenvolvimento: ligado. Feito UMA vez por máquina; depois vale o que o menu deixar.
            string key = "PROCEDURAL_GAME.DevToolsDefault." + Application.dataPath;
            if (EditorPrefs.HasKey(key)) return;

            EditorPrefs.SetBool(key, true);
            EditorApplication.delayCall += () => SetEnabled(true);
        }

        public static bool IsEnabled(NamedBuildTarget target) => Split(PlayerSettings.GetScriptingDefineSymbols(target)).Contains(Symbol);

        [MenuItem(MenuPath)]
        private static void Toggle() => SetEnabled(!IsEnabled(NamedBuildTarget.Android));

        [MenuItem(MenuPath, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, IsEnabled(NamedBuildTarget.Android));
            return true;
        }

        private static void SetEnabled(bool enabled)
        {
            foreach (NamedBuildTarget target in Targets)
            {
                string[] symbols = Split(PlayerSettings.GetScriptingDefineSymbols(target)).Where(s => s != Symbol).ToArray();
                if (enabled) symbols = symbols.Concat(new[] { Symbol }).ToArray();

                PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", symbols));
            }

            Debug.Log(enabled
                ? "Ferramentas DEV LIGADAS nos builds (símbolo DEV_TOOLS). Use só para testes."
                : "Ferramentas DEV DESLIGADAS nos builds: a compilação não inclui o código de calibração (versão comercial).");
        }

        private static string[] Split(string symbols) => symbols.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>Antes de cada build, diz no Console se ele leva as ferramentas DEV, para ninguém publicar um build de teste por engano.</summary>
    public class DevToolsBuildReport : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            NamedBuildTarget target = NamedBuildTarget.FromBuildTargetGroup(report.summary.platformGroup);

            if (DevToolsSwitch.IsEnabled(target))
            {
                Debug.LogWarning("[BUILD] Este build INCLUI as ferramentas DEV (DEV_TOOLS): botão DEV e calibração de árvores. " +
                                 "Para a versão comercial desmarque PROCEDURAL_GAME > Build > Dev Tools nos builds e confira se o Contador de FPS está desligado.");
            }
            else
            {
                Debug.Log("[BUILD] Build comercial: sem ferramentas DEV.");
            }
        }
    }
}
