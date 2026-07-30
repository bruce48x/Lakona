using System.Text.Json;
using Lakona.ProjectSystem.Packaging.Server;
using Xunit;

namespace Lakona.ProjectSystem.Tests.Packaging.Server;

public sealed class ServerPackageManifestTests
{
    [Fact]
    public void CreateV1_sets_fixed_manifest_flags()
    {
        var manifest = ServerPackageManifest.CreateV1(
            "v20260623-153045Z",
            new DateTimeOffset(2026, 6, 23, 15, 30, 45, TimeSpan.Zero),
            "linux-x64",
            "Release",
            "Server.App.dll",
            "20260612.001",
            "v20260623-153045Z",
            "0.14.0-test");

        Assert.True(manifest.SelfContained);
        Assert.False(manifest.Trimmed);
    }

    [Fact]
    public void CreateV1_normalizes_built_at_utc_to_utc_seconds()
    {
        var manifest = ServerPackageManifest.CreateV1(
            "v20260623-153045Z",
            new DateTimeOffset(2026, 6, 23, 23, 30, 45, TimeSpan.FromHours(8)).AddTicks(1234),
            "linux-x64",
            "Release",
            "Server.App.dll",
            "20260612.001",
            "v20260623-153045Z",
            "0.14.0-test");

        var expected = new DateTimeOffset(2026, 6, 23, 15, 30, 45, TimeSpan.Zero);
        Assert.Equal(expected, manifest.BuiltAtUtc);

        var json = JsonSerializer.Serialize(manifest, ServerJson.Options);
        Assert.Contains("\"builtAtUtc\": \"2026-06-23T15:30:45Z\"", json, StringComparison.Ordinal);

        var roundTripped = JsonSerializer.Deserialize<ServerPackageManifest>(json, ServerJson.Options);
        Assert.NotNull(roundTripped);
        Assert.Equal(expected, roundTripped.BuiltAtUtc);
        Assert.Equal(manifest, roundTripped);
    }

    [Fact]
    public void Manifest_serializes_with_web_casing_and_fixed_v1_flags()
    {
        var manifest = ServerPackageManifest.CreateV1(
            "v20260623-153045Z",
            new DateTimeOffset(2026, 6, 23, 15, 30, 45, TimeSpan.Zero),
            "linux-x64",
            "Release",
            "Server.App.dll",
            "20260612.001",
            "v20260623-153045Z",
            "0.14.0-test");

        var json = JsonSerializer.Serialize(manifest, ServerJson.Options);

        Assert.Contains("\"version\": \"v20260623-153045Z\"", json, StringComparison.Ordinal);
        Assert.Contains("\"builtAtUtc\": \"2026-06-23T15:30:45Z\"", json, StringComparison.Ordinal);
        Assert.Contains("\"runtime\": \"linux-x64\"", json, StringComparison.Ordinal);
        Assert.Contains("\"configuration\": \"Release\"", json, StringComparison.Ordinal);
        Assert.Contains("\"selfContained\": true", json, StringComparison.Ordinal);
        Assert.Contains("\"trimmed\": false", json, StringComparison.Ordinal);
        Assert.Contains("\"entryAssembly\": \"Server.App.dll\"", json, StringComparison.Ordinal);
        Assert.Contains("\"buildTag\": \"20260612.001\"", json, StringComparison.Ordinal);
        Assert.Contains("\"initialHotfixVersion\": \"v20260623-153045Z\"", json, StringComparison.Ordinal);
        Assert.Contains("\"toolVersion\": \"0.14.0-test\"", json, StringComparison.Ordinal);
    }
}
