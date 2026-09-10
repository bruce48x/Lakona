using System.Security.Cryptography;
using System.Text.Json;

namespace Lakona.ProjectSystem.Packaging.Hotfix;

internal sealed class HostRuntimeAssets
{
    private readonly Dictionary<string, string> files = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public static HostRuntimeAssets Read(string publishDirectory)
    {
        var root = Path.GetFullPath(publishDirectory);
        var manifests = Directory.GetFiles(root, "*.deps.json");
        if (manifests.Length == 0)
            throw new InvalidOperationException($"Host publish output '{root}' is missing its dependency manifest.");

        var assets = new HostRuntimeAssets();
        foreach (var manifest in manifests)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifest));
            if (!document.RootElement.TryGetProperty("targets", out var targets))
                throw new InvalidOperationException($"Host dependency manifest '{manifest}' has no runtime targets.");
            var entryName = Path.GetFileName(manifest)[..^".deps.json".Length];
            Add(Path.GetFileName(manifest));
            Add(entryName + ".runtimeconfig.json");
            Add(entryName + ".exe");
            Add(entryName);
            foreach (var target in targets.EnumerateObject())
            foreach (var library in target.Value.EnumerateObject())
            foreach (var section in new[] { "runtime", "native", "resources", "runtimeTargets" })
            {
                if (!library.Value.TryGetProperty(section, out var entries))
                    continue;
                foreach (var entry in entries.EnumerateObject())
                {
                    // Portable publishes keep runtimeTargets paths; RID-specific publishes
                    // flatten runtime/native assets. Satellite resources retain their locale.
                    var asset = entry.Name.Replace('\\', '/');
                    Add(asset);
                    Add(Path.GetFileName(asset));
                    if (entry.Value.TryGetProperty("locale", out var locale))
                        Add(locale.GetString() + "/" + Path.GetFileName(asset));
                }
            }
        }
        return assets;

        void Add(string relativePath)
        {
            var path = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            var relative = Path.GetRelativePath(root, path);
            if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidOperationException("Host dependency asset path escapes its publish directory.");
            if (!File.Exists(path))
                return;
            assets.files[relative.Replace('\\', '/')] = path;
            if (Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase))
            {
                var symbols = Path.ChangeExtension(path, ".pdb");
                if (File.Exists(symbols))
                    assets.files[Path.GetRelativePath(root, symbols).Replace('\\', '/')] = symbols;
            }
        }
    }

    public async Task<bool> IsDuplicateAsync(string relativePath, string candidatePath, CancellationToken cancellationToken)
    {
        if (!files.TryGetValue(relativePath.Replace('\\', '/'), out var hostPath)
            || new FileInfo(hostPath).Length != new FileInfo(candidatePath).Length)
            return false;

        await using var host = File.OpenRead(hostPath);
        await using var candidate = File.OpenRead(candidatePath);
        var hostHash = await SHA256.HashDataAsync(host, cancellationToken).ConfigureAwait(false);
        var candidateHash = await SHA256.HashDataAsync(candidate, cancellationToken).ConfigureAwait(false);
        return hostHash.AsSpan().SequenceEqual(candidateHash);
    }
}
