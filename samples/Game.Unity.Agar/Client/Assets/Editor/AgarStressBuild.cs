#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;

internal static class AgarStressBuild
{
    private const string OutputOption = "-buildOutput";
    private const string PlatformOption = "-buildPlatform";

    public static void BuildClient()
    {
        var outputPath = Path.GetFullPath(ReadRequiredOption(OutputOption));
        var platform = ReadRequiredOption(PlatformOption);
        var buildTarget = platform.ToLowerInvariant() switch
        {
            "windows" => BuildTarget.StandaloneWindows64,
            "macos" => BuildTarget.StandaloneOSX,
            "linux" => BuildTarget.StandaloneLinux64,
            _ => throw new ArgumentException($"Unsupported Agar stress-client build platform: {platform}.")
        };
        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var scenes = EditorBuildSettings.scenes
            .Where(static scene => scene.enabled)
            .Select(static scene => scene.path)
            .ToArray();
        if (scenes.Length == 0)
        {
            throw new InvalidOperationException("No enabled scenes are configured for the Agar client build.");
        }

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = buildTarget,
            options = BuildOptions.None
        });

        if (report.summary.result != BuildResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Agar stress client build failed: result={report.summary.result}, errors={report.summary.totalErrors}.");
        }
    }

    private static string ReadRequiredOption(string optionName)
    {
        var args = Environment.GetCommandLineArgs();
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], optionName, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(args[index + 1]))
            {
                return args[index + 1];
            }
        }

        throw new ArgumentException($"Required Unity command-line option is missing: {optionName} <path>.");
    }
}
#endif
