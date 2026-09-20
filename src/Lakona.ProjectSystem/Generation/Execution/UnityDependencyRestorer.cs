using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Lakona.ProjectSystem.Generation.Domain;
using Lakona.ProjectSystem.Generation.Planning;
using Lakona.ProjectSystem.Generation.Rendering.Client;

namespace Lakona.ProjectSystem.Generation.Execution;

internal sealed class UnityDependencyRestorer : IUnityDependencyRestorer
{
    private const string NuGetForUnityResource = "Lakona.ProjectSystem.Generation.Rendering.Client.TemplateAssets.NuGetForUnity.4.5.0.zip";
    private static readonly TimeSpan RestoreTimeout = TimeSpan.FromMinutes(10);

    public async Task<RestoredUnityDependencies?> RestoreAsync(
        LakonaProjectSpec spec,
        GenerationPlan plan,
        CancellationToken cancellationToken)
    {
        if (!ClientEnginePolicy.IsUnityCompatible(spec.ClientEngine))
        {
            return null;
        }

        var editor = await UnityEditorLocator.FindAsync(spec, cancellationToken).ConfigureAwait(false)
            ?? throw new LakonaProjectCreationException(
                $"A compatible {DisplayName(spec)} editor is not installed. " +
                "Install it or set UNITY_PATH/TUANJIE_PATH to its executable before creating the project.");
        var bootstrapRoot = Path.Combine(Path.GetTempPath(), "Lakona.ProjectSystem.Restore", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(bootstrapRoot);

        try
        {
            var bootstrapSpec = spec with
            {
                ClientEditorVersion = editor.Version,
                ClientEditorRevision = editor.Revision
            };
            var bootstrapPlan = plan with
            {
                Files = plan.Files
                    .Select(file => file.RelativePath.Equals(
                            "Client/ProjectSettings/ProjectVersion.txt",
                            StringComparison.OrdinalIgnoreCase)
                        ? file with
                        {
                            Content = UnityClientRenderer.RenderProjectVersion(bootstrapSpec)
                        }
                        : file)
                    .ToArray()
            };
            CreateBootstrapProject(bootstrapPlan, bootstrapRoot);
            await RunRestoreAsync(editor.ExecutablePath, bootstrapRoot, cancellationToken).ConfigureAwait(false);
            var packagesRoot = Path.Combine(bootstrapRoot, "Assets", "Packages");
            VerifyPackages(bootstrapPlan, packagesRoot);
            return new RestoredUnityDependencies(
                packagesRoot,
                bootstrapRoot,
                editor.Version,
                editor.Revision);
        }
        catch
        {
            if (Directory.Exists(bootstrapRoot))
            {
                Directory.Delete(bootstrapRoot, recursive: true);
            }

            throw;
        }
    }

    private static void CreateBootstrapProject(GenerationPlan plan, string root)
    {
        CopyPlanFile(plan, "Client/ProjectSettings/ProjectVersion.txt", root, "ProjectSettings/ProjectVersion.txt");
        CopyPlanFile(plan, "Client/Assets/packages.config", root, "Assets/packages.config");
        CopyPlanFile(plan, "Client/Assets/NuGet.config", root, "Assets/NuGet.config");

        var packageRoot = Path.Combine(root, "Packages");
        Directory.CreateDirectory(packageRoot);
        using var stream = typeof(UnityDependencyRestorer).Assembly.GetManifestResourceStream(NuGetForUnityResource)
            ?? throw new LakonaProjectCreationException("Embedded NuGetForUnity bootstrap package is missing.");
        ZipFile.ExtractToDirectory(stream, packageRoot);
        File.WriteAllText(Path.Combine(packageRoot, "manifest.json"),
            "{\n  \"dependencies\": {\n    \"com.github-glitchenzo.nugetforunity\": \"file:com.github-glitchenzo.nugetforunity\"\n  }\n}\n");

        var editorRoot = Path.Combine(packageRoot, "com.github-glitchenzo.nugetforunity", "Editor");
        File.WriteAllText(Path.Combine(editorRoot, "LakonaRestoreEntrypoint.cs"), RestoreEntrypointSource);
    }

    private static void CopyPlanFile(GenerationPlan plan, string planPath, string root, string destinationPath)
    {
        var generated = plan.Files.SingleOrDefault(file => file.RelativePath.Equals(planPath, StringComparison.OrdinalIgnoreCase))
            ?? throw new LakonaProjectCreationException($"Bootstrap input is missing from the generation plan: {planPath}");
        var destination = Path.Combine(root, destinationPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllText(destination, generated.Content);
    }

    private static async Task RunRestoreAsync(string executable, string projectRoot, CancellationToken cancellationToken)
    {
        var logPath = Path.Combine(projectRoot, "restore.log");
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in new[]
                 {
                     "-batchmode", "-nographics", "-quit", "-projectPath", projectRoot,
                     "-executeMethod", "NugetForUnity.LakonaRestoreEntrypoint.Run", "-logFile", logPath
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process process;
        try
        {
            process = Process.Start(startInfo)
                ?? throw new LakonaProjectCreationException($"Unable to start editor: {executable}");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new LakonaProjectCreationException($"Unable to start editor '{executable}': {ex.Message}", ex);
        }

        using (process)
        using (var timeout = new CancellationTokenSource(RestoreTimeout))
        using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token))
        {
            try
            {
                await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                process.Kill(entireProcessTree: true);
                throw new LakonaProjectCreationException($"Editor dependency restore timed out after {RestoreTimeout.TotalMinutes:0} minutes.");
            }

            if (process.ExitCode != 0 || !File.Exists(Path.Combine(projectRoot, "LakonaNuGetRestore.success")))
            {
                throw new LakonaProjectCreationException(
                    $"Editor dependency restore failed with exit code {process.ExitCode}.{Environment.NewLine}{ReadLogTail(logPath)}");
            }
        }
    }

    private static void VerifyPackages(GenerationPlan plan, string packagesRoot)
    {
        var config = plan.Files.Single(file => file.RelativePath.Equals("Client/Assets/packages.config", StringComparison.OrdinalIgnoreCase));
        var expected = XDocument.Parse(config.Content).Root!.Elements("package")
            .Select(element => $"{element.Attribute("id")!.Value}.{element.Attribute("version")!.Value}")
            .ToArray();
        var missing = expected.Where(name => !ContainsRestoredFiles(Path.Combine(packagesRoot, name))).ToArray();
        if (missing.Length > 0)
        {
            throw new LakonaProjectCreationException($"NuGet restore did not produce: {string.Join(", ", missing)}");
        }
    }

    private static bool ContainsRestoredFiles(string packagePath) =>
        Directory.Exists(packagePath) && Directory.EnumerateFiles(packagePath, "*", SearchOption.AllDirectories).Any();

    private static string ReadLogTail(string logPath)
    {
        if (!File.Exists(logPath))
        {
            return "The editor did not produce a restore log.";
        }

        return string.Join(Environment.NewLine, File.ReadLines(logPath).TakeLast(40));
    }

    private static string DisplayName(LakonaProjectSpec spec) => spec.ClientEngine switch
    {
        ClientEngine.Tuanjie => $"Tuanjie {ClientEngineVersions.Tuanjie}",
        _ => spec.ClientEngineVersion switch
        {
            ClientEngineVersion.Unity2022 => $"Unity {ClientEngineVersions.Unity2022}",
            ClientEngineVersion.Unity60 => $"Unity {ClientEngineVersions.Unity60}",
            ClientEngineVersion.Unity63 => $"Unity {ClientEngineVersions.Unity63}",
            _ => "Unity"
        }
    };

    private const string RestoreEntrypointSource = """
        using System;
        using System.IO;
        using System.Linq;
        using System.Xml.Linq;
        using UnityEditor;
        using UnityEngine;

        namespace NugetForUnity
        {
            public static class LakonaRestoreEntrypoint
            {
                public static void Run()
                {
                    try
                    {
                        PackageRestorer.Restore(true);
                        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                        var packages = XDocument.Load(Path.Combine(Application.dataPath, "packages.config"))
                            .Root.Elements("package")
                            .Select(element => element.Attribute("id").Value + "." + element.Attribute("version").Value);
                        var root = Path.Combine(Application.dataPath, "Packages");
                        var missing = packages.Where(name =>
                            !Directory.Exists(Path.Combine(root, name)) ||
                            !Directory.EnumerateFiles(Path.Combine(root, name), "*", SearchOption.AllDirectories).Any()).ToArray();
                        if (missing.Length > 0)
                        {
                            throw new InvalidOperationException("Missing restored packages: " + string.Join(", ", missing));
                        }

                        File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "LakonaNuGetRestore.success"), "ok");
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                        EditorApplication.Exit(1);
                    }
                }
            }
        }
        """;
}

internal static partial class UnityEditorLocator
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);

    public static async Task<UnityEditorInstallation?> FindAsync(
        LakonaProjectSpec spec,
        CancellationToken cancellationToken)
    {
        var expected = spec.ClientEngine == ClientEngine.Tuanjie
            ? ClientEngineVersions.TuanjieUnityEditor
            : spec.ClientEngineVersion switch
            {
                ClientEngineVersion.Unity2022 => ClientEngineVersions.Unity2022,
                ClientEngineVersion.Unity60 => ClientEngineVersions.Unity60,
                ClientEngineVersion.Unity63 => ClientEngineVersions.Unity63,
                _ => null
            };
        if (expected is null)
        {
            return null;
        }

        var executableName = GetExecutableName(spec);
        var explicitPath = Environment.GetEnvironmentVariable(spec.ClientEngine == ClientEngine.Tuanjie ? "TUANJIE_PATH" : "UNITY_PATH");
        foreach (var candidate in Candidates(spec, expected, executableName, explicitPath)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            var installation = await ProbeAsync(candidate, cancellationToken).ConfigureAwait(false);
            if (SelectInstallation(installation, candidate, expected) is { } selected)
            {
                return selected;
            }
        }

        return null;
    }

    internal static UnityEditorInstallation? SelectInstallation(
        UnityEditorInstallation? probed,
        string candidatePath,
        string expectedVersion)
    {
        if (probed is not null)
        {
            // A probed version is authoritative. Falling back to the directory
            // name after a real incompatible result would launch that binary
            // anyway and record a version that never executed.
            return UnityEditorVersion.IsCompatible(probed.Version, expectedVersion) ? probed : null;
        }

        // Probing could not determine a version, so the installation directory
        // name is the only remaining evidence for this candidate.
        var pathVersion = ReadVersionFromPath(candidatePath);
        return pathVersion is not null && UnityEditorVersion.IsCompatible(pathVersion, expectedVersion)
            ? new UnityEditorInstallation(Path.GetFullPath(candidatePath), pathVersion, null)
            : null;
    }

    private static IEnumerable<string> Candidates(LakonaProjectSpec spec, string version, string executableName, string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(spec.ClientEditorPath))
        {
            yield return spec.ClientEditorPath;
        }

        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            yield return explicitPath;
        }

        var homeVariable = spec.ClientEngine == ClientEngine.Tuanjie ? "TUANJIE_HOME" : "UNITY_HOME";
        if (Environment.GetEnvironmentVariable(homeVariable) is { Length: > 0 } home)
        {
            yield return Path.Combine(home, "Editor", executableName);
            yield return Path.Combine(home, executableName);
        }

        if (OperatingSystem.IsWindows())
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var product = spec.ClientEngine == ClientEngine.Tuanjie ? "Tuanjie" : "Unity";
            yield return Path.Combine(programFiles, product, "Hub", "Editor", version, "Editor", executableName);
            var editorRoot = Path.Combine(programFiles, product, "Hub", "Editor");
            if (Directory.Exists(editorRoot))
            {
                foreach (var directory in Directory.EnumerateDirectories(editorRoot))
                {
                    yield return Path.Combine(directory, "Editor", executableName);
                }
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return $"/Applications/{(spec.ClientEngine == ClientEngine.Tuanjie ? "Tuanjie" : "Unity")}/Hub/Editor/{version}/{(spec.ClientEngine == ClientEngine.Tuanjie ? "Tuanjie" : "Unity")}.app/Contents/MacOS/{(spec.ClientEngine == ClientEngine.Tuanjie ? "Tuanjie" : "Unity")}";
        }
    }

    private static string GetExecutableName(LakonaProjectSpec spec) =>
        spec.ClientEngine == ClientEngine.Tuanjie
            ? OperatingSystem.IsWindows() ? "Tuanjie.exe" : "Tuanjie"
            : OperatingSystem.IsWindows() ? "Unity.exe" : "Unity";

    private static async Task<UnityEditorInstallation?> ProbeAsync(
        string executablePath,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[] { "-batchmode", "-nographics", "-quit", "-logFile", "-" })
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }

        if (process is null)
        {
            return null;
        }

        using (process)
        using (var timeout = new CancellationTokenSource(ProbeTimeout))
        using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token))
        {
            var outputTask = process.StandardOutput.ReadToEndAsync(linked.Token);
            var errorTask = process.StandardError.ReadToEndAsync(linked.Token);
            try
            {
                await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
                var output = await outputTask.ConfigureAwait(false);
                var error = await errorTask.ConfigureAwait(false);
                return ParseInstallation(executablePath, output + Environment.NewLine + error);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                return null;
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                throw;
            }
        }
    }

    private static UnityEditorInstallation? ParseInstallation(string executablePath, string output)
    {
        var match = EditorVersionPattern().Match(output);
        if (!match.Success)
        {
            return null;
        }

        return new UnityEditorInstallation(
            Path.GetFullPath(executablePath),
            match.Groups["version"].Value,
            match.Groups["revision"].Success ? match.Groups["revision"].Value : null);
    }

    private static string? ReadVersionFromPath(string executablePath)
    {
        foreach (var segment in executablePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (UnityVersionPattern().IsMatch(segment))
            {
                return segment;
            }
        }

        return null;
    }

    [GeneratedRegex(
        @"Unity(?: Editor)? version:\s*(?<version>\d+\.\d+\.\d+[a-z]\d+(?:[a-z]\d+)*)(?:\s+\((?<revision>[0-9a-f]{12})\))?",
        RegexOptions.IgnoreCase)]
    private static partial Regex EditorVersionPattern();

    [GeneratedRegex(@"^\d+\.\d+\.\d+[a-z]\d+(?:[a-z]\d+)*$", RegexOptions.IgnoreCase)]
    private static partial Regex UnityVersionPattern();
}
