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

            await VerifyChecksumsAsync(staging, cancellationToken).ConfigureAwait(false);
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

    private static async Task VerifyChecksumsAsync(string directory, CancellationToken cancellationToken)
    {
        var checksumPath = Path.Combine(directory, "checksums.sha256");
        if (!File.Exists(checksumPath))
        {
            throw new InvalidOperationException("Hotfix package is missing checksums.sha256.");
        }

        var lines = await File.ReadAllLinesAsync(checksumPath, cancellationToken).ConfigureAwait(false);
        foreach (var line in lines.Where(static line => !string.IsNullOrWhiteSpace(line)))
        {
            var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                throw new InvalidOperationException("Hotfix checksum file is invalid.");
            }

            var path = Path.Combine(directory, parts[1]);
            await using var stream = File.OpenRead(path);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
            if (!StringComparer.OrdinalIgnoreCase.Equals(parts[0], actual))
            {
                throw new InvalidOperationException($"Checksum mismatch for '{parts[1]}'.");
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
}
