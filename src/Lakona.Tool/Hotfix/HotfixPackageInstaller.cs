using System.IO.Compression;
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

            await HotfixPackageVerifier.VerifyChecksumsAsync(
                staging,
                manifest.Assembly,
                cancellationToken).ConfigureAwait(false);
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

}
