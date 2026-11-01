using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PROCEDURAL_GAME.EditorTools
{
    /// <summary>
    /// Contorna o erro de build Android "Burst requires the Android NDK ... ANDROID_NDK_ROOT is undefined".
    /// O Burst não conseguia descobrir o NDK embutido no Unity; quando isso falha ele usa a variável de ambiente
    /// ANDROID_NDK_ROOT, então a definimos (só dentro do processo do Editor) apontando para o NDK que vem com o Unity.
    /// Se a variável já existir, não mexe. Se o problema deixar de ocorrer em outra versão do Unity/Burst, pode apagar este arquivo.
    /// </summary>
    [InitializeOnLoad]
    public static class AndroidNdkEnvironment
    {
        private const string VariableName = "ANDROID_NDK_ROOT";

        static AndroidNdkEnvironment()
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(VariableName))) return;

            string playbackEngine = BuildPipeline.GetPlaybackEngineDirectory(BuildTarget.Android, BuildOptions.None);
            if (string.IsNullOrEmpty(playbackEngine)) return;

            string ndkRoot = Path.GetFullPath(Path.Combine(playbackEngine, "NDK"));
            if (!Directory.Exists(ndkRoot)) return;

            Environment.SetEnvironmentVariable(VariableName, ndkRoot);
            Debug.Log($"{VariableName} definido para este Editor: {ndkRoot}");
        }
    }
}
