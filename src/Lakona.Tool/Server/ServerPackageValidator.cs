using System.Text.Json;
using Lakona.Tool.Hotfix;

namespace Lakona.Tool.Server;

internal sealed class ServerPackageValidator
{
    public async Task ValidateAsync(
        string appDirectory,
        ServerPackageManifest manifest,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDirectory);
        ArgumentNullException.ThrowIfNull(manifest);

        var root = Path.GetFullPath(appDirectory);
        if (!Directory.Exists(root))
        {
            throw new InvalidOperationException($"Server package directory '{root}' does not exist.");
        }

        RejectBuildOutputDirectories(root);
        RejectReloadSignalFiles(root);
        RequireRootFile(root, manifest.EntryAssembly, "entry assembly");
        var serverManifestPath = Path.Combine(root, "lakona-server.json");
        RequireFile(serverManifestPath, "server package manifest");
        var fileManifest = await ReadServerManifestAsync(serverManifestPath, cancellationToken).ConfigureAwait(false);
        EnsureManifestMatches(fileManifest, manifest);

        var hotfixRoot = Path.Combine(root, "hotfix");
        var currentPath = Path.Combine(hotfixRoot, "current.txt");
        RequireFile(currentPath, "current hotfix pointer");
        var currentVersion = (await File.ReadAllTextAsync(currentPath, cancellationToken).ConfigureAwait(false)).Trim();
        if (!StringComparer.Ordinal.Equals(currentVersion, manifest.InitialHotfixVersion))
        {
            throw new InvalidOperationException(
                $"Server package current hotfix version '{currentVersion}' does not match manifest initial hotfix version '{manifest.InitialHotfixVersion}'.");
        }

        HotfixPackageVerifier.ValidateVersionName(manifest.InitialHotfixVersion);
        var versionDirectory = Path.Combine(hotfixRoot, "versions", manifest.InitialHotfixVersion);
        if (!Directory.Exists(versionDirectory))
        {
            throw new InvalidOperationException($"Server package is missing initial hotfix directory '{manifest.InitialHotfixVersion}'.");
        }

        RequireFile(Path.Combine(versionDirectory, "READY"), "initial hotfix READY marker");
        var hotfixManifestPath = Path.Combine(versionDirectory, "hotfix.json");
        RequireFile(hotfixManifestPath, "initial hotfix manifest");
        RequireFile(Path.Combine(versionDirectory, "checksums.sha256"), "initial hotfix checksums");

        await using var stream = File.OpenRead(hotfixManifestPath);
        var hotfixManifest = await JsonSerializer.DeserializeAsync<HotfixPackageManifest>(
            stream,
            HotfixJson.Options,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Initial hotfix manifest is invalid.");

        if (!StringComparer.Ordinal.Equals(hotfixManifest.BuildTag, manifest.BuildTag))
        {
            throw new InvalidOperationException(
                $"Initial hotfix BuildTag '{hotfixManifest.BuildTag}' does not match server BuildTag '{manifest.BuildTag}'.");
        }

        if (!StringComparer.Ordinal.Equals(hotfixManifest.Version, manifest.InitialHotfixVersion))
        {
            throw new InvalidOperationException(
                $"Initial hotfix version '{hotfixManifest.Version}' does not match server manifest initial hotfix version '{manifest.InitialHotfixVersion}'.");
        }

        await HotfixPackageVerifier.VerifyChecksumsAsync(
            versionDirectory,
            hotfixManifest.Assembly,
            allowReadyMarker: true,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ServerPackageManifest> ReadServerManifestAsync(
        string manifestPath,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(manifestPath);
            return await JsonSerializer.DeserializeAsync<ServerPackageManifest>(
                stream,
                ServerJson.Options,
                cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Server package manifest is invalid.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Server package manifest is invalid.", exception);
        }
    }

    private static void EnsureManifestMatches(ServerPackageManifest fileManifest, ServerPackageManifest expectedManifest)
    {
        if (!StringComparer.Ordinal.Equals(fileManifest.Version, expectedManifest.Version)
            || fileManifest.BuiltAtUtc != expectedManifest.BuiltAtUtc
            || !StringComparer.Ordinal.Equals(fileManifest.Runtime, expectedManifest.Runtime)
            || !StringComparer.Ordinal.Equals(fileManifest.Configuration, expectedManifest.Configuration)
            || fileManifest.SelfContained != expectedManifest.SelfContained
            || fileManifest.Trimmed != expectedManifest.Trimmed
            || !StringComparer.Ordinal.Equals(fileManifest.EntryAssembly, expectedManifest.EntryAssembly)
            || !StringComparer.Ordinal.Equals(fileManifest.BuildTag, expectedManifest.BuildTag)
            || !StringComparer.Ordinal.Equals(fileManifest.InitialHotfixVersion, expectedManifest.InitialHotfixVersion)
            || !StringComparer.Ordinal.Equals(fileManifest.ToolVersion, expectedManifest.ToolVersion))
        {
            throw new InvalidOperationException("Server package manifest file does not match the expected manifest.");
        }
    }

    private static void RejectBuildOutputDirectories(string root)
    {
        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(directory);
            if (!StringComparer.OrdinalIgnoreCase.Equals(name, "bin")
                && !StringComparer.OrdinalIgnoreCase.Equals(name, "obj"))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(root, directory);
            throw new InvalidOperationException(
                $"Server package contains build output directory '{relativePath}'. Remove bin/obj directories before packing.");
        }
    }

    private static void RejectReloadSignalFiles(string root)
    {
        foreach (var file in Directory.EnumerateFiles(root, "reload.signal", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(root, file);
            throw new InvalidOperationException(
                $"Server package contains reload signal file '{relativePath}'. Remove reload.signal before packing.");
        }
    }

    private static void RequireRootFile(string root, string fileName, string description)
    {
        if (string.IsNullOrWhiteSpace(fileName)
            || Path.IsPathRooted(fileName)
            || !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Server package {description} path is invalid.");
        }

        RequireFile(Path.Combine(root, fileName), description);
    }

    private static void RequireFile(string path, string description)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Server package is missing {description} '{Path.GetFileName(path)}'.");
        }
    }
}
