using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Workerborn.Editor
{
    public static class WorkerbornDedicatedBuild
    {
        [MenuItem("Workerborn/Build/Dedicated Server (Linux)")]
        public static void BuildLinux() => Build(BuildTarget.StandaloneLinux64, "Build/WorkerbornServer/WorkerbornServer");

        [MenuItem("Workerborn/Build/Dedicated Server (Windows)")]
        public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, "Build/WorkerbornServer-Windows/WorkerbornServer.exe");

        private static void Build(BuildTarget target, string fallback)
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, target))
                throw new BuildFailedException("Install Dedicated Server Build Support for the selected target in Unity Hub.");
            var arguments = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(arguments, "-workerborn-output");
            string output = index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : fallback;
            output = Path.GetFullPath(output);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var scenes = new[]
            {
                "Assets/Workerborn/Scenes/MainMenu.unity",
                "Assets/Workerborn/Scenes/VillagePrototype.unity"
            };
            if (scenes.Any(scene => !File.Exists(scene)))
                throw new BuildFailedException("The Workerborn scenes are missing.");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                target = target,
                subtarget = (int)StandaloneBuildSubtarget.Server,
                locationPathName = output
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException("Workerborn dedicated server build did not succeed.");
        }
    }
}
