using System.Text.Json;

namespace Lakona.Hub.Updates;

internal sealed record HubReleaseManifest(
    int SchemaVersion,
    string Version,
    string Tag,
    DateTimeOffset PublishedAtUtc,
    string Repository,
    Dictionary<string, HubReleasePlatform> Platforms)
{
    public const int CurrentSchemaVersion = 1;

    public static HubReleaseManifest Parse(string json)
    {
        var manifest = JsonSerializer.Deserialize(json, HubJsonContext.Default.HubReleaseManifest)
            ?? throw new InvalidDataException("The update manifest is empty.");
        if (manifest.SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException($"Unsupported update manifest schema {manifest.SchemaVersion}.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Version) ||
            string.IsNullOrWhiteSpace(manifest.Tag) ||
            string.IsNullOrWhiteSpace(manifest.Repository) ||
            manifest.Platforms is null ||
            manifest.Platforms.Count == 0)
        {
            throw new InvalidDataException("The update manifest is incomplete.");
        }

        foreach (var platform in manifest.Platforms.Values)
        {
            if (platform is null ||
                string.IsNullOrWhiteSpace(platform.PackageRoot) ||
                string.IsNullOrWhiteSpace(platform.ExecutablePath) ||
                platform.Full is null ||
                platform.Deltas is null)
            {
                throw new InvalidDataException("The update manifest contains an incomplete platform entry.");
            }
        }

        return manifest;
    }

    internal static JsonSerializerOptions JsonOptions => HubJsonContext.Default.Options;
}

internal sealed record HubReleasePlatform(
    string PackageRoot,
    string ExecutablePath,
    HubReleaseAsset Full,
    IReadOnlyList<HubReleaseDelta> Deltas);

internal sealed record HubReleaseAsset(string AssetName, string Sha256, long Size);

internal sealed record HubReleaseDelta(string FromVersion, string AssetName, string Sha256, long Size);
