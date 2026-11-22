using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Workerborn.Editor
{
    public static class WorkerbornDedicatedBuild
    {
        private const string WindowsServer = "Build/WorkerbornServer-Windows/WorkerbornServer.exe";
        private const string ManagerExecutable = "WorkerbornServerManager.exe";

        [MenuItem("Workerborn/Build/Dedicated Server (Linux)")]
        public static void BuildLinux() => Build(BuildTarget.StandaloneLinux64, OutputPath("Build/WorkerbornServer/WorkerbornServer"));

        [MenuItem("Workerborn/Build/Dedicated Server (Windows)")]
        public static void BuildWindows()
        {
            string output = OutputPath(WindowsServer);
            if (string.Equals(Path.GetFileName(output), ManagerExecutable, StringComparison.OrdinalIgnoreCase))
                throw new BuildFailedException("Choose a different filename for the headless server executable.");
            string manager = PublishManager();
            Build(BuildTarget.StandaloneWindows64, output);
            string folder = Path.GetDirectoryName(output);
            File.Copy(manager, Path.Combine(folder, ManagerExecutable), overwrite: true);
            File.WriteAllText(Path.Combine(folder, "WorkerbornServerManager.host"), Path.GetFileName(output));
        }

        public static void BuildWindowsHeadless() => Build(BuildTarget.StandaloneWindows64, OutputPath(WindowsServer));

        private static string OutputPath(string fallback)
        {
            var arguments = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(arguments, "-workerborn-output");
            string output = index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : fallback;
            return Path.GetFullPath(output);
        }

        private static void Build(BuildTarget target, string output)
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, target))
                throw new BuildFailedException("Install Dedicated Server Build Support for the selected target in Unity Hub.");
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

        private static string PublishManager()
        {
            string source = Path.GetFullPath("Tools/ServerManager");
            var start = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "publish WorkerbornServerManager.csproj --configuration Release",
                WorkingDirectory = source,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            try
            {
                using var process = Process.Start(start);
                var output = process.StandardOutput.ReadToEndAsync();
                var errors = process.StandardError.ReadToEndAsync();
                process.WaitForExit();
                if (process.ExitCode != 0)
                    throw new BuildFailedException("Could not publish the Workerborn server manager.\n" + output.GetAwaiter().GetResult() + errors.GetAwaiter().GetResult());
            }
            catch (Win32Exception)
            {
                throw new BuildFailedException("Install the .NET 8 SDK and make dotnet available to Unity to build the Windows server manager.");
            }

            string executable = Path.Combine(source, "bin", "Release", "net8.0-windows", "win-x64", "publish", ManagerExecutable);
            if (!File.Exists(executable))
                throw new BuildFailedException("The Windows server manager executable was not produced.");
            return executable;
        }
    }
}
