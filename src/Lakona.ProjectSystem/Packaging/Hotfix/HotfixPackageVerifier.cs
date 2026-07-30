using System.Security.Cryptography;

namespace Lakona.ProjectSystem.Packaging.Hotfix;

internal static class HotfixPackageVerifier
{
    public static void ValidateVersionName(string version)
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

    public static async Task VerifyChecksumsAsync(
        string directory,
        string assemblyFileName,
        bool allowReadyMarker,
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
        RejectUnchecksummedFiles(directory, checksumPath, checksums, allowReadyMarker);

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

    private static void RejectUnchecksummedFiles(
        string directory,
        string checksumPath,
        IReadOnlyDictionary<string, ChecksumEntry> checksums,
        bool allowReadyMarker)
    {
        var checksumFullPath = Path.GetFullPath(checksumPath);
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            var fullPath = Path.GetFullPath(file);
            if (StringComparer.Ordinal.Equals(fullPath, checksumFullPath)
                || allowReadyMarker && IsReadyMarker(directory, fullPath))
            {
                continue;
            }

            var relativePath = NormalizeRelativePath(Path.GetRelativePath(directory, fullPath));
            if (!checksums.ContainsKey(relativePath))
            {
                throw new InvalidOperationException($"Hotfix checksum file is missing '{relativePath}'.");
            }
        }
    }

    private static bool IsReadyMarker(string directory, string fullPath)
    {
        var readyPath = Path.GetFullPath(Path.Combine(directory, "READY"));
        return StringComparer.Ordinal.Equals(fullPath, readyPath);
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
