using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lakona.Tool.Hotfix;

internal sealed class HotfixPackageInstaller
{
    public async Task<string> InstallAsync(string zipPath, string root, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        var operationId = Guid.NewGuid().ToString("N");
        var staging = Path.Combine(root, "staging", operationId);
        Directory.CreateDirectory(staging);
        try
        {
            ZipFile.ExtractToDirectory(zipPath, staging);
            var manifestPath = Path.Combine(staging, "hotfix.json");
            await using var stream = File.OpenRead(manifestPath);
            var manifest = await JsonSerializer.DeserializeAsync<HotfixPackageManifest>(
                stream,
                HotfixJson.Options,
                cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Hotfix package manifest is invalid.");
            ValidateVersionName(manifest.Version);

            await VerifyChecksumsAsync(staging, manifest.Assembly, cancellationToken).ConfigureAwait(false);
            var target = Path.Combine(root, "versions", manifest.Version);
            if (Directory.Exists(target))
            {
                if (await SameChecksumsAsync(staging, target, cancellationToken).ConfigureAwait(false))
                {
                    return manifest.Version;
                }

                throw new InvalidOperationException($"Hotfix version '{manifest.Version}' already exists with different content.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            try
            {
                CopyDirectory(staging, target);
                await File.WriteAllTextAsync(Path.Combine(target, "READY"), "", cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                if (Directory.Exists(target) && !File.Exists(Path.Combine(target, "READY")))
                {
                    Directory.Delete(target, recursive: true);
                }

                throw;
            }

            return manifest.Version;
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }

    private static async Task VerifyChecksumsAsync(
        string directory,
        string assemblyFileName,
        CancellationToken cancellationToken)
    {
        var checksumPath = Path.Combine(directory, "checksums.sha256");
        if (!File.Exists(checksumPath))
        {
            throw new InvalidOperationException("Hotfix package is missing checksums.sha256.");
        }

        var lines = await File.ReadAllLinesAsync(checksumPath, cancellationToken).ConfigureAwait(false);
        var checksums = ParseChecksums(directory, lines);
        RequireChecksum(checksums, "hotfix.json");
        RequireChecksum(checksums, assemblyFileName);

        foreach (var item in checksums.Values)
        {
            if (!File.Exists(item.FullPath))
            {
                throw new InvalidOperationException($"Hotfix package is missing '{item.RelativePath}'.");
            }

            await using var stream = File.OpenRead(item.FullPath);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
            if (!StringComparer.OrdinalIgnoreCase.Equals(item.Hash, actual))
            {
                throw new InvalidOperationException($"Checksum mismatch for '{item.RelativePath}'.");
            }
        }
    }

    private static async Task<bool> SameChecksumsAsync(string left, string right, CancellationToken cancellationToken)
    {
        var leftText = await File.ReadAllTextAsync(Path.Combine(left, "checksums.sha256"), cancellationToken).ConfigureAwait(false);
        var rightText = await File.ReadAllTextAsync(Path.Combine(right, "checksums.sha256"), cancellationToken).ConfigureAwait(false);
        return StringComparer.Ordinal.Equals(leftText, rightText);
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
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)), overwrite: false);
        }
    }

    private static void ValidateVersionName(string version)
    {
        if (string.IsNullOrWhiteSpace(version)
            || Path.IsPathRooted(version)
            || version.Contains(Path.DirectorySeparatorChar)
            || version.Contains(Path.AltDirectorySeparatorChar)
            || version.Contains("..", StringComparison.Ordinal)
            || version.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || !string.Equals(Path.GetFileName(version), version, StringComparison.Ordinal))
        {
            throw new ArgumentException("Invalid hotfix package version.", nameof(version));
        }
    }

    private static Dictionary<string, ChecksumEntry> ParseChecksums(
        string directory,
        IReadOnlyList<string> lines)
    {
        var entries = new Dictionary<string, ChecksumEntry>(PathComparer);
        foreach (var line in lines.Where(static line => !string.IsNullOrWhiteSpace(line)))
        {
            var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                throw new InvalidOperationException("Hotfix checksum file is invalid.");
            }

            var relativePath = NormalizeSeparators(parts[1]);
            if (Path.IsPathRooted(relativePath) || IsRootedWithAnySeparator(relativePath))
            {
                throw new InvalidOperationException("Hotfix checksum path is invalid.");
            }

            var fullPath = Path.GetFullPath(Path.Combine(directory, relativePath));
            if (!IsUnderDirectory(directory, fullPath))
            {
                throw new InvalidOperationException("Hotfix checksum path is invalid.");
            }

            var normalized = NormalizeRelativePath(relativePath);
            if (!entries.TryAdd(normalized, new ChecksumEntry(parts[0], normalized, fullPath)))
            {
                throw new InvalidOperationException($"Duplicate checksum entry '{normalized}'.");
            }
        }

        if (entries.Count == 0)
        {
            throw new InvalidOperationException("Hotfix checksum file is empty.");
        }

        return entries;
    }

    private static void RequireChecksum(
        IReadOnlyDictionary<string, ChecksumEntry> checksums,
        string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        if (!checksums.ContainsKey(normalized))
        {
            throw new InvalidOperationException($"Hotfix checksum file is missing '{normalized}'.");
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
