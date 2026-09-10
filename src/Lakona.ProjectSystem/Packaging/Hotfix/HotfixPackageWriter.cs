using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Lakona.ProjectSystem.Packaging;
using Lakona.ProjectSystem.Packaging.Server;

namespace Lakona.ProjectSystem.Packaging.Hotfix;

internal sealed class HotfixPackageWriter
{
    private readonly IDotNetCommandRunner dotNet;

    public HotfixPackageWriter(IDotNetCommandRunner? dotNet = null)
    {
        this.dotNet = dotNet ?? new DotNetCommandRunner();
    }

    public async Task<string> PackAsync(
        string projectPath,
        string outputDirectory,
        string configuration,
        string version,
        CancellationToken cancellationToken = default,
        string? hostProjectPath = null,
        string? publishedHostDirectory = null,
        string? runtimeIdentifier = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        var fullProjectPath = Path.GetFullPath(projectPath);
        var project = LoadProject(fullProjectPath);
        var workDirectory = Path.Combine(Path.GetFullPath(outputDirectory), ".staging", Guid.NewGuid().ToString("N"));
        var publishDirectory = Path.Combine(workDirectory, "hotfix");
        Directory.CreateDirectory(publishDirectory);
        try
        {
            if (publishedHostDirectory is null && hostProjectPath is not null)
            {
                publishedHostDirectory = Path.Combine(workDirectory, "host");
                await PublishAsync(Path.GetFullPath(hostProjectPath), configuration, publishedHostDirectory, runtimeIdentifier, cancellationToken).ConfigureAwait(false);
            }
            await PublishAsync(fullProjectPath, configuration, publishDirectory, runtimeIdentifier, cancellationToken).ConfigureAwait(false);
            return await WritePackageAsync(
                publishDirectory,
                outputDirectory,
                project.AssemblyName,
                project.TargetFramework,
                BuildTagReader.Read(fullProjectPath),
                version,
                DateTimeOffset.UtcNow,
                cancellationToken,
                publishedHostDirectory).ConfigureAwait(false);
        }
        finally
        {
            Directory.Delete(workDirectory, recursive: true);
        }
    }

    internal async Task<string> WritePackageAsync(
        string buildOutputDirectory,
        string outputDirectory,
        string assemblyName,
        string targetFramework,
        string buildTag,
        string version,
        DateTimeOffset builtAtUtc,
        CancellationToken cancellationToken = default,
        string? publishedHostDirectory = null)
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
            var assemblyPath = Path.Combine(buildOutputDirectory, assemblyFile);
            if (!File.Exists(assemblyPath))
            {
                throw new FileNotFoundException($"Hotfix publish output is missing '{assemblyFile}'.", assemblyPath);
            }
            // This is a fresh SDK publish output, not bin: preserve its resolved runtime
            // assets and relative paths, including native libraries and satellite assemblies.
            var sharedAssets = publishedHostDirectory is null
                ? null
                : HostRuntimeAssets.Read(publishedHostDirectory);
            foreach (var file in Directory.EnumerateFiles(buildOutputDirectory, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(buildOutputDirectory, file);
                if (!StringComparer.OrdinalIgnoreCase.Equals(relativePath, assemblyFile)
                    && sharedAssets is not null
                    && await sharedAssets.IsDuplicateAsync(relativePath, file, cancellationToken).ConfigureAwait(false))
                    continue;
                var destination = Path.Combine(staging, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination, overwrite: true);
            }

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
                await JsonSerializer.SerializeAsync(
                    stream,
                    manifest,
                    HotfixJson.Context.HotfixPackageManifest,
                    cancellationToken).ConfigureAwait(false);
            }

            await WriteChecksumsAsync(staging, cancellationToken).ConfigureAwait(false);

            var zipPath = Path.Combine(
                outputDirectory,
                $"Server.Hotfix-{buildTag}-{version}.zip");
            if (File.Exists(zipPath) || Directory.Exists(zipPath))
            {
                throw new InvalidOperationException($"Package already exists: '{zipPath}'.");
            }

            var temporaryZipPath = Path.Combine(
                outputDirectory,
                $".{Path.GetFileName(zipPath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                ZipFile.CreateFromDirectory(staging, temporaryZipPath);
                try
                {
                    File.Move(temporaryZipPath, zipPath);
                }
                catch (IOException exception) when (File.Exists(zipPath) || Directory.Exists(zipPath))
                {
                    throw new InvalidOperationException($"Package already exists: '{zipPath}'.", exception);
                }

                return zipPath;
            }
            finally
            {
                if (File.Exists(temporaryZipPath))
                {
                    File.Delete(temporaryZipPath);
                }
            }
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }

    private async Task PublishAsync(
        string projectPath,
        string configuration,
        string publishDirectory,
        string? runtimeIdentifier,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            "publish", projectPath, "-c", configuration, "-o", publishDirectory,
            "--self-contained", "false", "-p:UseAppHost=false", "-p:PublishTrimmed=false",
            "-p:PublishSingleFile=false", "-p:PublishAot=false", "/nologo"
        };
        if (runtimeIdentifier is not null)
            arguments.AddRange(["-r", runtimeIdentifier]);
        var result = await dotNet.RunAsync(
            Path.GetDirectoryName(projectPath)!,
            arguments,
            cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"dotnet publish failed for '{projectPath}'.{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}{result.StandardError}");
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

    private static async Task WriteChecksumsAsync(string directory, CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        foreach (var file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var name = Path.GetRelativePath(directory, file).Replace('\\', '/');
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
