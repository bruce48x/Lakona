using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Lakona.ProjectSystem.Generation.Domain;
using Lakona.ProjectSystem.Generation.Planning;

namespace Lakona.ProjectSystem.Generation.Execution;

internal sealed class UnityDependencyRestorer(Func<ProcessStartInfo, Process?> startEditor, TimeSpan restoreTimeout) : IUnityDependencyRestorer
{
    private const string NuGetForUnityResource = "Lakona.ProjectSystem.Generation.Rendering.Client.TemplateAssets.NuGetForUnity.4.5.0.zip";
    public UnityDependencyRestorer() : this(Process.Start, TimeSpan.FromMinutes(10)) { }

    public async Task<UnityEditorInstallation> ResolveEditorAsync(
        LakonaProjectSpec spec,
        CancellationToken cancellationToken)
    {
        if (!ClientEnginePolicy.IsUnityCompatible(spec.ClientEngine))
        {
            throw new ArgumentException("Only Unity-compatible clients require an editor.", nameof(spec));
        }

        return await UnityEditorLocator.FindAsync(spec, cancellationToken).ConfigureAwait(false)
            ?? throw new LakonaProjectCreationException(
                $"A compatible {DisplayName(spec)} editor is not installed. " +
                "Install it or set UNITY_PATH/TUANJIE_PATH to its executable before creating the project.");
    }

    public async Task<RestoredUnityDependencies> RestoreAsync(
        GenerationPlan plan,
        UnityEditorInstallation editor,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bootstrapRoot = Path.Combine(Path.GetTempPath(), "Lakona.ProjectSystem.Restore", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(bootstrapRoot);

        try
        {
            CreateBootstrapProject(plan, bootstrapRoot);
            await RunRestoreAsync(editor.ExecutablePath, bootstrapRoot, cancellationToken).ConfigureAwait(false);
            var packagesRoot = Path.Combine(bootstrapRoot, "Assets", "Packages");
            VerifyPackages(plan, packagesRoot);
            return new RestoredUnityDependencies(packagesRoot, bootstrapRoot);
        }
        catch (EditorStopException ex)
        {
            // The editor may still be using this directory.
            throw new LakonaProjectCreationException(ex.Message, ex);
        }
        catch (Exception failure)
        {
            if (Directory.Exists(bootstrapRoot))
            {
                try { Directory.Delete(bootstrapRoot, recursive: true); }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
                {
                    throw new LakonaProjectCreationException(
                        $"Restore failed and the temporary project could not be removed: {bootstrapRoot}",
                        new AggregateException(failure, cleanup));
                }
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

    private async Task RunRestoreAsync(string executable, string projectRoot, CancellationToken cancellationToken)
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
            cancellationToken.ThrowIfCancellationRequested();
            process = startEditor(startInfo)
                ?? throw new LakonaProjectCreationException($"Unable to start editor: {executable}");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new LakonaProjectCreationException($"Unable to start editor '{executable}': {ex.Message}", ex);
        }

        using (process)
        using (var timeout = new CancellationTokenSource(restoreTimeout))
        using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token))
        {
            try
            {
                await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await StopEditorAsync(process, projectRoot).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                throw new LakonaProjectCreationException($"Editor dependency restore timed out after {restoreTimeout.TotalMinutes:0.##} minutes.");
            }

            if (process.ExitCode != 0 || !File.Exists(Path.Combine(projectRoot, "LakonaNuGetRestore.success")))
            {
                throw new LakonaProjectCreationException(
                    $"Editor dependency restore failed with exit code {process.ExitCode}.{Environment.NewLine}{ReadLogTail(logPath)}");
            }
        }
    }

    internal static async Task StopEditorAsync(Process process, string projectRoot)
    {
        try
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (Exception ex) when ((ex is InvalidOperationException or System.ComponentModel.Win32Exception) && process.HasExited)
            {
                // The editor exited between HasExited and Kill.
            }

            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new EditorStopException(
                $"Could not confirm editor process {process.Id} stopped. Temporary project retained at '{projectRoot}'.", ex);
        }
    }

    private sealed class EditorStopException(string message, Exception cause) : Exception(message, cause);

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
            var product = spec.ClientEngine == ClientEngine.Tuanjie ? "Tuanjie" : "Unity";
            foreach (var candidate in VersionedEditorCandidates(
                         $"/Applications/{product}/Hub/Editor",
                         version,
                         $"{product}.app/Contents/MacOS/{product}"))
            {
                yield return candidate;
            }
        }
    }

    internal static IReadOnlyList<string> VersionedEditorCandidates(
        string editorRoot,
        string exactVersion,
        string relativeExecutablePath)
    {
        var relativePath = relativeExecutablePath.Replace('/', Path.DirectorySeparatorChar);
        var candidates = new List<string>
        {
            Path.Combine(editorRoot, exactVersion, relativePath)
        };
        if (!Directory.Exists(editorRoot))
        {
            return candidates;
        }

        try
        {
            candidates.AddRange(Directory.EnumerateDirectories(editorRoot)
                .Select(directory => Path.Combine(directory, relativePath)));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // The exact candidate remains useful when the installation root
            // cannot be enumerated.
        }

        return candidates;
    }

    private static string GetExecutableName(LakonaProjectSpec spec) =>
        spec.ClientEngine == ClientEngine.Tuanjie
            ? OperatingSystem.IsWindows() ? "Tuanjie.exe" : "Tuanjie"
            : OperatingSystem.IsWindows() ? "Unity.exe" : "Unity";

    internal static async Task<UnityEditorInstallation?> ProbeAsync(
        string executablePath,
        CancellationToken cancellationToken,
        Func<ProcessStartInfo, Process?>? startEditor = null,
        TimeSpan? probeTimeout = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        executablePath = Path.GetFullPath(executablePath);
        var projectRoot = Path.Combine(Path.GetTempPath(), "Lakona.ProjectSystem.Probe", Guid.NewGuid().ToString("N"));
        var safeToDelete = true;
        try
        {
            Directory.CreateDirectory(Path.Combine(projectRoot, "Assets"));
            Directory.CreateDirectory(Path.Combine(projectRoot, "ProjectSettings"));
            var startInfo = new ProcessStartInfo(executablePath)
            {
                WorkingDirectory = projectRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in new[] { "-batchmode", "-nographics", "-quit", "-projectPath", projectRoot, "-logFile", "-" })
            {
                startInfo.ArgumentList.Add(argument);
            }

            Process? process;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                process = (startEditor ?? Process.Start)(startInfo);
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                return null;
            }
            if (process is null) return null;

            safeToDelete = false;
            using (process)
            using (var timeout = new CancellationTokenSource(probeTimeout ?? ProbeTimeout))
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token))
            {
                var outputTask = process.StandardOutput.ReadToEndAsync(linked.Token);
                var errorTask = process.StandardError.ReadToEndAsync(linked.Token);
                var reads = Task.WhenAll(outputTask, errorTask);
                try
                {
                    await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
                    var output = await reads.ConfigureAwait(false);
                    return ParseInstallation(executablePath, string.Join(Environment.NewLine, output));
                }
                catch (OperationCanceledException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return null;
                }
                finally
                {
                    try
                    {
                        await UnityDependencyRestorer.StopEditorAsync(process, projectRoot).ConfigureAwait(false);
                        safeToDelete = true;
                    }
                    finally
                    {
                        linked.Cancel();
                        // Join both redirected readers, including the interrupted path.
                        try { await reads.ConfigureAwait(false); }
                        catch (Exception ex) when (ex is OperationCanceledException or IOException) { }
                    }
                }
            }
        }
        finally
        {
            // If stopping failed, retain the directory rather than deleting a live project.
            if (safeToDelete && Directory.Exists(projectRoot))
            {
                try { Directory.Delete(projectRoot, recursive: true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Trace.TraceWarning("Could not remove editor probe project '{0}': {1}", projectRoot, ex.Message);
                }
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
        @"(?:Unity(?: Editor)? version:\s*|^Built from '[^'\r\n]+' branch; Version is ')(?<version>\d+\.\d+\.\d+[a-z]\d+(?:[a-z]\d+)*)(?:\s+\((?<revision>[0-9a-f]{12})\))?",
        RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex EditorVersionPattern();

    [GeneratedRegex(@"^\d+\.\d+\.\d+[a-z]\d+(?:[a-z]\d+)*$", RegexOptions.IgnoreCase)]
    private static partial Regex UnityVersionPattern();
}
