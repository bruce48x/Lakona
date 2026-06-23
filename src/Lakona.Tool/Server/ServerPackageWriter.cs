using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Lakona.Tool.Hotfix;

namespace Lakona.Tool.Server;

internal sealed class ServerPackageWriter
{
    private readonly HotfixPackageInstaller hotfixInstaller;
    private readonly ServerPackageValidator validator;

    public ServerPackageWriter(HotfixPackageInstaller? hotfixInstaller = null, ServerPackageValidator? validator = null)
    {
        this.hotfixInstaller = hotfixInstaller ?? new HotfixPackageInstaller();
        this.validator = validator ?? new ServerPackageValidator();
    }

    public async Task<string> WritePackageFromPublishedAppAsync(
        ServerPackageWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PublishedAppDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.HotfixPackagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.EntryAssembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RuntimeIdentifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Version);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BuildTag);
        ValidateFileNameComponent(request.EntryAssembly, nameof(request.EntryAssembly));
        ValidateFileNameComponent(request.RuntimeIdentifier, nameof(request.RuntimeIdentifier));
        ValidateFileNameComponent(request.Version, nameof(request.Version));

        var publishedAppDirectory = Path.GetFullPath(request.PublishedAppDirectory);
        var hotfixPackagePath = Path.GetFullPath(request.HotfixPackagePath);
        var outputDirectory = Path.GetFullPath(request.OutputDirectory);

        if (!Directory.Exists(publishedAppDirectory))
        {
            throw new InvalidOperationException($"Published app directory '{publishedAppDirectory}' does not exist.");
        }

        if (!File.Exists(hotfixPackagePath))
        {
            throw new InvalidOperationException($"Hotfix package '{hotfixPackagePath}' does not exist.");
        }

        if (IsSameOrChildPath(publishedAppDirectory, outputDirectory))
        {
            throw new InvalidOperationException("OutputDirectory must not be inside the published app directory.");
        }

        Directory.CreateDirectory(outputDirectory);
        var stagingRoot = Path.Combine(outputDirectory, ".staging", Guid.NewGuid().ToString("N"));
        var stagedApp = Path.Combine(stagingRoot, "app");
        try
        {
            CopyDirectory(publishedAppDirectory, stagedApp);

            var hotfixRoot = Path.Combine(stagedApp, "hotfix");
            if (Directory.Exists(hotfixRoot))
            {
                Directory.Delete(hotfixRoot, recursive: true);
            }
            else if (File.Exists(hotfixRoot))
            {
                File.Delete(hotfixRoot);
            }

            var installedVersion = await hotfixInstaller.InstallAsync(
                hotfixPackagePath,
                hotfixRoot,
                cancellationToken).ConfigureAwait(false);
            if (!StringComparer.Ordinal.Equals(installedVersion, request.Version))
            {
                throw new InvalidOperationException(
                    $"Initial hotfix version '{installedVersion}' does not match server version '{request.Version}'.");
            }

            await File.WriteAllTextAsync(
                Path.Combine(hotfixRoot, "current.txt"),
                request.Version,
                cancellationToken).ConfigureAwait(false);

            var versionDirectory = Path.Combine(hotfixRoot, "versions", request.Version);
            _ = await ReadHotfixManifestAsync(versionDirectory, cancellationToken).ConfigureAwait(false);

            var manifest = ServerPackageManifest.CreateV1(
                request.Version,
                request.BuiltAtUtc,
                request.RuntimeIdentifier,
                request.Configuration,
                request.EntryAssembly,
                request.BuildTag,
                request.Version,
                GetToolVersion());
            await using (var stream = File.Create(Path.Combine(stagedApp, "lakona-server.json")))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    manifest,
                    ServerJson.Options,
                    cancellationToken).ConfigureAwait(false);
            }

            await validator.ValidateAsync(stagedApp, manifest, cancellationToken).ConfigureAwait(false);

            var finalZipPath = GetZipPath(outputDirectory, request.EntryAssembly, request.Version, request.RuntimeIdentifier);
            var temporaryZipPath = Path.Combine(outputDirectory, $".{Path.GetFileName(finalZipPath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                if (File.Exists(temporaryZipPath))
                {
                    File.Delete(temporaryZipPath);
                }

                ZipFile.CreateFromDirectory(stagedApp, temporaryZipPath);
                File.Move(temporaryZipPath, finalZipPath, overwrite: true);
                return finalZipPath;
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
            if (Directory.Exists(stagingRoot))
            {
                Directory.Delete(stagingRoot, recursive: true);
            }
        }
    }

    private static void ValidateFileNameComponent(string value, string parameterName)
    {
        if (Path.IsPathRooted(value)
            || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || value.Contains('/')
            || value.Contains('\\')
            || value.Contains(Path.DirectorySeparatorChar)
            || value.Contains(Path.AltDirectorySeparatorChar)
            || !StringComparer.Ordinal.Equals(Path.GetFileName(value), value)
            || StringComparer.Ordinal.Equals(value, ".")
            || StringComparer.Ordinal.Equals(value, ".."))
        {
            throw new ArgumentException("Value must be a single file name component.", parameterName);
        }
    }

    private static bool IsSameOrChildPath(string parent, string candidate)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var normalizedParent = TrimEndingDirectorySeparators(parent);
        var normalizedCandidate = TrimEndingDirectorySeparators(candidate);
        return string.Equals(normalizedParent, normalizedCandidate, comparison)
            || normalizedCandidate.StartsWith(
                normalizedParent + Path.DirectorySeparatorChar,
                comparison);
    }

    private static string TrimEndingDirectorySeparators(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)), overwrite: true);
        }
    }

    private static string GetZipPath(
        string outputDirectory,
        string entryAssembly,
        string version,
        string runtimeIdentifier)
    {
        var entryName = Path.GetFileNameWithoutExtension(entryAssembly);
        return Path.Combine(outputDirectory, $"{entryName}-{version}-{runtimeIdentifier}.zip");
    }

    private static string GetToolVersion()
    {
        return typeof(ServerPackageWriter).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?? typeof(ServerPackageWriter).Assembly.GetName().Version?.ToString()
            ?? "0.0.0-local";
    }

    private static async Task<HotfixPackageManifest> ReadHotfixManifestAsync(
        string versionDirectory,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(Path.Combine(versionDirectory, "hotfix.json"));
        return await JsonSerializer.DeserializeAsync<HotfixPackageManifest>(
            stream,
            HotfixJson.Options,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Initial hotfix manifest is invalid.");
    }
}
