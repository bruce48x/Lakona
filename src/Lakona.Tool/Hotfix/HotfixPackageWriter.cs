using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

namespace Lakona.Tool.Hotfix;

internal sealed class HotfixPackageWriter
{
    public async Task<string> PackAsync(
        string projectPath,
        string outputDirectory,
        string configuration,
        string version,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        var fullProjectPath = Path.GetFullPath(projectPath);
        var project = LoadProject(fullProjectPath);
        await BuildAsync(fullProjectPath, configuration, cancellationToken).ConfigureAwait(false);

        var buildOutputDirectory = Path.Combine(
            Path.GetDirectoryName(fullProjectPath)!,
            "bin",
            configuration,
            project.TargetFramework);

        return await WritePackageAsync(
            buildOutputDirectory,
            outputDirectory,
            project.AssemblyName,
            project.TargetFramework,
            ReadBuildTag(Path.GetDirectoryName(fullProjectPath)!),
            version,
            DateTimeOffset.UtcNow,
            cancellationToken).ConfigureAwait(false);
    }

    internal async Task<string> WritePackageAsync(
        string buildOutputDirectory,
        string outputDirectory,
        string assemblyName,
        string targetFramework,
        string buildTag,
        string version,
        DateTimeOffset builtAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(buildOutputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFramework);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        Directory.CreateDirectory(outputDirectory);
        var staging = Path.Combine(outputDirectory, ".staging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var assemblyFile = assemblyName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                ? assemblyName
                : assemblyName + ".dll";
            CopyRequiredFile(buildOutputDirectory, staging, assemblyFile);
            CopyOptionalFile(buildOutputDirectory, staging, Path.ChangeExtension(assemblyFile, ".pdb"));
            CopyOptionalFile(buildOutputDirectory, staging, Path.ChangeExtension(assemblyFile, ".deps.json"));

            var manifest = new HotfixPackageManifest(
                version,
                builtAtUtc,
                assemblyFile,
                targetFramework,
                buildTag,
                GetToolVersion());
            var manifestPath = Path.Combine(staging, "hotfix.json");
            await using (var stream = File.Create(manifestPath))
            {
                await JsonSerializer.SerializeAsync(stream, manifest, HotfixJson.Options, cancellationToken).ConfigureAwait(false);
            }

            await WriteChecksumsAsync(staging, cancellationToken).ConfigureAwait(false);

            var zipPath = Path.Combine(outputDirectory, $"{Path.GetFileNameWithoutExtension(assemblyFile)}-{version}.zip");
            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            ZipFile.CreateFromDirectory(staging, zipPath);
            return zipPath;
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }

    private static async Task BuildAsync(string projectPath, string configuration, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("build");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(configuration);
        startInfo.ArgumentList.Add("/nologo");

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start dotnet build.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"dotnet build failed for hotfix project.{Environment.NewLine}{output}{Environment.NewLine}{error}");
        }
    }

    private static HotfixProjectInfo LoadProject(string projectPath)
    {
        var document = XDocument.Load(projectPath);
        var targetFramework = document.Descendants("TargetFramework").FirstOrDefault()?.Value;
        if (string.IsNullOrWhiteSpace(targetFramework))
        {
            throw new InvalidOperationException("Hotfix project must define TargetFramework.");
        }

        var assemblyName = document.Descendants("AssemblyName").FirstOrDefault()?.Value;
        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            assemblyName = Path.GetFileNameWithoutExtension(projectPath);
        }

        return new HotfixProjectInfo(assemblyName, targetFramework);
    }

    private static string ReadBuildTag(string projectDirectory)
    {
        var candidates = new[]
        {
            Path.Combine(projectDirectory, "BuildTag.props"),
            Path.GetFullPath(Path.Combine(projectDirectory, "..", "App", "BuildTag.props"))
        };
        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            var document = XDocument.Load(candidate);
            var value = document.Descendants("LakonaHotfixBuildTag").FirstOrDefault()?.Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return "";
    }

    private static void CopyRequiredFile(string sourceDirectory, string targetDirectory, string fileName)
    {
        var source = Path.Combine(sourceDirectory, fileName);
        if (!File.Exists(source))
        {
            throw new FileNotFoundException($"Hotfix build output is missing '{fileName}'.", source);
        }

        File.Copy(source, Path.Combine(targetDirectory, fileName), overwrite: true);
    }

    private static void CopyOptionalFile(string sourceDirectory, string targetDirectory, string fileName)
    {
        var source = Path.Combine(sourceDirectory, fileName);
        if (File.Exists(source))
        {
            File.Copy(source, Path.Combine(targetDirectory, fileName), overwrite: true);
        }
    }

    private static async Task WriteChecksumsAsync(string directory, CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        foreach (var file in Directory.GetFiles(directory).OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(file);
            if (StringComparer.Ordinal.Equals(name, "checksums.sha256"))
            {
                continue;
            }

            await using var stream = File.OpenRead(file);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
            lines.Add($"{hash} {name}");
        }

        await File.WriteAllLinesAsync(Path.Combine(directory, "checksums.sha256"), lines, cancellationToken).ConfigureAwait(false);
    }

    private static string GetToolVersion()
    {
        return typeof(HotfixPackageWriter).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?? typeof(HotfixPackageWriter).Assembly.GetName().Version?.ToString()
            ?? "0.0.0-local";
    }

    private sealed record HotfixProjectInfo(string AssemblyName, string TargetFramework);
}
