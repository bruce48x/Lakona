using System.Text.Json;
using Lakona.Tool.Server;
using Xunit;

namespace Lakona.Tool.Tests.Server;

public sealed class ServerPackageManifestTests
{
    [Fact]
    public void Manifest_serializes_with_web_casing_and_fixed_v1_flags()
    {
        var manifest = new ServerPackageManifest(
            "v20260623-153045Z",
            new DateTimeOffset(2026, 6, 23, 15, 30, 45, TimeSpan.Zero),
            "linux-x64",
            "Release",
            selfContained: true,
            trimmed: false,
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
