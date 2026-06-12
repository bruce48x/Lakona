using System.Security.Cryptography;
using System.Text.Json;

namespace Lakona.Game.Server.HotfixAdmin;

public sealed class HotfixVersionStore
{
    private readonly string _root;

    public HotfixVersionStore(string root)
    {
        _root = string.IsNullOrWhiteSpace(root) ? throw new ArgumentException("Value cannot be empty.", nameof(root)) : root;
    }

    public async Task<string?> ReadPointerAsync(string fileName, CancellationToken cancellationToken = default)
    {
        var path = SafeRootFile(fileName);
        if (!File.Exists(path))
        {
            return null;
        }

        var value = (await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)).Trim();
        return value.Length == 0 ? null : value;
    }

    public async Task WritePointerAsync(string fileName, string? version, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_root);
        var path = SafeRootFile(fileName);
        await File.WriteAllTextAsync(path, version ?? "", cancellationToken).ConfigureAwait(false);
    }

    public async Task<HotfixPackageManifest> ReadManifestAsync(string version, CancellationToken cancellationToken = default)
    {
        ValidateVersionName(version);
        var directory = VersionDirectory(version);
        if (!File.Exists(Path.Combine(directory, "READY")))
        {
            throw new InvalidOperationException($"Hotfix version '{version}' is missing READY.");
        }

        var manifestPath = Path.Combine(directory, "hotfix.json");
        if (!File.Exists(manifestPath))
        {
            throw new InvalidOperationException($"Hotfix version '{version}' is missing hotfix.json.");
        }

        await using var stream = File.OpenRead(manifestPath);
        var manifest = await JsonSerializer.DeserializeAsync<HotfixPackageManifest>(
            stream,
            HotfixAdminJson.Options,
            cancellationToken).ConfigureAwait(false);
        return manifest ?? throw new InvalidOperationException($"Hotfix version '{version}' has an invalid manifest.");
    }

    public async Task ValidateChecksumsAsync(string version, CancellationToken cancellationToken = default)
    {
        ValidateVersionName(version);
        var directory = VersionDirectory(version);
        var checksumPath = Path.Combine(directory, "checksums.sha256");
        if (!File.Exists(checksumPath))
        {
            throw new InvalidOperationException($"Hotfix version '{version}' is missing checksums.sha256.");
        }

        var root = Path.GetFullPath(directory);
        var lines = await File.ReadAllLinesAsync(checksumPath, cancellationToken).ConfigureAwait(false);
        foreach (var line in lines.Where(static line => !string.IsNullOrWhiteSpace(line)))
        {
            var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                throw new InvalidOperationException($"Hotfix version '{version}' has an invalid checksum file.");
            }

            var path = Path.GetFullPath(Path.Combine(directory, parts[1]));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Hotfix version '{version}' checksum path is invalid.");
            }

            if (!File.Exists(path))
            {
                throw new InvalidOperationException($"Hotfix version '{version}' is missing '{parts[1]}'.");
            }

            await using var stream = File.OpenRead(path);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
            if (!StringComparer.OrdinalIgnoreCase.Equals(parts[0], actual))
            {
                throw new InvalidOperationException($"Checksum mismatch for '{parts[1]}'.");
            }
        }
    }

    public string VersionDirectory(string version)
    {
        ValidateVersionName(version);
        return Path.Combine(_root, "versions", version);
    }

    private string SafeRootFile(string fileName)
    {
        if (fileName is not "current.txt" and not "previous.txt")
        {
            throw new ArgumentException("Unsupported hotfix pointer file.", nameof(fileName));
        }

        return Path.Combine(_root, fileName);
    }

    private static void ValidateVersionName(string version)
    {
        if (string.IsNullOrWhiteSpace(version)
            || version.Contains(Path.DirectorySeparatorChar)
            || version.Contains(Path.AltDirectorySeparatorChar)
            || version.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("Invalid hotfix version name.", nameof(version));
        }
    }
}
