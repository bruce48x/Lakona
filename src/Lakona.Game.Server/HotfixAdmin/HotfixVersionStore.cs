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
        var manifest = await ReadManifestAsync(version, cancellationToken).ConfigureAwait(false);
        var directory = VersionDirectory(version);
        var checksumPath = Path.Combine(directory, "checksums.sha256");
        if (!File.Exists(checksumPath))
        {
            throw new InvalidOperationException($"Hotfix version '{version}' is missing checksums.sha256.");
        }

        var lines = await File.ReadAllLinesAsync(checksumPath, cancellationToken).ConfigureAwait(false);
        var checksums = ParseChecksums(version, directory, lines);
        RequireChecksum(version, checksums, "hotfix.json");
        RequireChecksum(version, checksums, manifest.Assembly);

        foreach (var item in checksums.Values)
        {
            if (!File.Exists(item.FullPath))
            {
                throw new InvalidOperationException($"Hotfix version '{version}' is missing '{item.RelativePath}'.");
            }

            await using var stream = File.OpenRead(item.FullPath);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
            if (!StringComparer.OrdinalIgnoreCase.Equals(item.Hash, actual))
            {
                throw new InvalidOperationException($"Checksum mismatch for '{item.RelativePath}'.");
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

    private static Dictionary<string, ChecksumEntry> ParseChecksums(
        string version,
        string directory,
        IReadOnlyList<string> lines)
    {
        var entries = new Dictionary<string, ChecksumEntry>(PathComparer);
        foreach (var line in lines.Where(static line => !string.IsNullOrWhiteSpace(line)))
        {
            var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                throw new InvalidOperationException($"Hotfix version '{version}' has an invalid checksum file.");
            }

            var relativePath = NormalizeSeparators(parts[1]);
            if (Path.IsPathRooted(relativePath) || IsRootedWithAnySeparator(relativePath))
            {
                throw new InvalidOperationException($"Hotfix version '{version}' checksum path is invalid.");
            }

            var fullPath = Path.GetFullPath(Path.Combine(directory, relativePath));
            if (!IsUnderDirectory(directory, fullPath))
            {
                throw new InvalidOperationException($"Hotfix version '{version}' checksum path is invalid.");
            }

            var normalized = NormalizeRelativePath(relativePath);
            if (!entries.TryAdd(normalized, new ChecksumEntry(parts[0], normalized, fullPath)))
            {
                throw new InvalidOperationException($"Duplicate checksum entry '{normalized}' in hotfix version '{version}'.");
            }
        }

        if (entries.Count == 0)
        {
            throw new InvalidOperationException($"Hotfix version '{version}' checksum file is empty.");
        }

        return entries;
    }

    private static void RequireChecksum(
        string version,
        IReadOnlyDictionary<string, ChecksumEntry> checksums,
        string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        if (!checksums.ContainsKey(normalized))
        {
            throw new InvalidOperationException($"Hotfix version '{version}' checksums.sha256 is missing '{normalized}'.");
        }
    }

    private static bool IsUnderDirectory(string directory, string path)
    {
        var root = Path.GetFullPath(directory);
        var rootWithSeparator = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return path.StartsWith(rootWithSeparator, PathComparison);
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        return NormalizeSeparators(relativePath).Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string NormalizeSeparators(string path)
    {
        return path
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
    }

    private static bool IsRootedWithAnySeparator(string path)
    {
        return path.StartsWith(Path.DirectorySeparatorChar)
            || path.StartsWith($"{Path.DirectorySeparatorChar}{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && IsAnySeparator(path[2]);
    }

    private static bool IsAnySeparator(char value)
    {
        return value is '/' or '\\';
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private sealed record ChecksumEntry(string Hash, string RelativePath, string FullPath);
}
