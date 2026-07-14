using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lakona.Hub.Updates;
using Xunit;

namespace Lakona.Hub.Tests;

public sealed class HubUpdateServiceTests
{
    [Fact]
    public async Task CheckAsync_PrefersDeltaFromExactCurrentVersion()
    {
        using var fixture = new UpdateFeedFixture("1.2.0", "win-x64", "1.3.0", deltaFrom: "1.2.0");
        var service = fixture.CreateService();

        var update = await service.CheckAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(update);
        Assert.Equal("1.3.0", update.Version);
        Assert.True(update.IsDelta);
        Assert.Equal("hub.delta.zip", update.Asset.AssetName);
    }

    [Fact]
    public async Task CheckAsync_FallsBackToFullPackageWithoutMatchingDelta()
    {
        using var fixture = new UpdateFeedFixture("1.1.0", "linux-x64", "1.3.0", deltaFrom: "1.2.0");
        var service = fixture.CreateService();

        var update = await service.CheckAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(update);
        Assert.False(update.IsDelta);
        Assert.Equal("hub-full.zip", update.Asset.AssetName);
    }

    [Fact]
    public async Task CheckAsync_ReturnsNullWhenInstalledVersionIsCurrent()
    {
        using var fixture = new UpdateFeedFixture("1.3.0", "osx-arm64", "1.3.0", deltaFrom: "1.2.0");

        var update = await fixture.CreateService().CheckAsync(TestContext.Current.CancellationToken);

        Assert.Null(update);
    }

    [Fact]
    public async Task PrepareCandidateAsync_AppliesChangedAndDeletedFilesThenValidatesTarget()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lakona-hub-update-test-{Guid.NewGuid():N}");
        var install = Path.Combine(root, "install");
        var delta = Path.Combine(root, "delta");
        var candidate = Path.Combine(root, "candidate");
        var archive = Path.Combine(root, "delta.zip");
        try
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            Directory.CreateDirectory(install);
            Directory.CreateDirectory(delta);
            await File.WriteAllTextAsync(Path.Combine(install, "unchanged.txt"), "same", cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(install, "changed.txt"), "old", cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(install, "removed.txt"), "remove me", cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(delta, "changed.txt"), "new", cancellationToken);

            var package = new HubPackageManifest(
                1,
                "2.0.0",
                [
                    PackageFile("unchanged.txt", "same"),
                    PackageFile("changed.txt", "new")
                ],
                []);
            await File.WriteAllTextAsync(
                Path.Combine(delta, "hub-package.json"),
                JsonSerializer.Serialize(package, HubReleaseManifest.JsonOptions),
                cancellationToken);
            var deltaManifest = new HubDeltaManifest(1, "1.0.0", "2.0.0", ["removed.txt"]);
            await File.WriteAllTextAsync(
                Path.Combine(delta, "hub-delta.json"),
                JsonSerializer.Serialize(deltaManifest, HubReleaseManifest.JsonOptions),
                cancellationToken);
            ZipFile.CreateFromDirectory(delta, archive);

            var plan = new HubUpdateLaunchPlan(
                install,
                archive,
                "Lakona Hub",
                "Lakona.Hub.exe",
                "1.0.0",
                "2.0.0",
                true,
                0);
            await HubUpdateInstaller.PrepareCandidateAsync(plan, candidate);

            Assert.Equal("same", await File.ReadAllTextAsync(Path.Combine(candidate, "unchanged.txt"), cancellationToken));
            Assert.Equal("new", await File.ReadAllTextAsync(Path.Combine(candidate, "changed.txt"), cancellationToken));
            Assert.False(File.Exists(Path.Combine(candidate, "removed.txt")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task PrepareCandidateAsync_ExtractsFullPackageRootAndValidatesTargetVersion()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lakona-hub-full-update-test-{Guid.NewGuid():N}");
        var package = Path.Combine(root, "archive", "Lakona Hub");
        var archive = Path.Combine(root, "full.zip");
        var candidate = Path.Combine(root, "candidate");
        try
        {
            Directory.CreateDirectory(package);
            await File.WriteAllTextAsync(
                Path.Combine(package, "Lakona.Hub.exe"),
                "new app",
                TestContext.Current.CancellationToken);
            var manifest = new HubPackageManifest(
                1,
                "3.0.0",
                [PackageFile("Lakona.Hub.exe", "new app")],
                []);
            await File.WriteAllTextAsync(
                Path.Combine(package, "hub-package.json"),
                JsonSerializer.Serialize(manifest, HubReleaseManifest.JsonOptions),
                TestContext.Current.CancellationToken);
            ZipFile.CreateFromDirectory(Path.Combine(root, "archive"), archive);

            var plan = new HubUpdateLaunchPlan(
                Path.Combine(root, "install"),
                archive,
                "Lakona Hub",
                "Lakona.Hub.exe",
                "2.0.0",
                "3.0.0",
                false,
                0);
            await HubUpdateInstaller.PrepareCandidateAsync(plan, candidate);

            Assert.Equal("new app", await File.ReadAllTextAsync(
                Path.Combine(candidate, "Lakona.Hub.exe"),
                TestContext.Current.CancellationToken));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void SafeDestination_RejectsZipSlipPaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "lakona-hub-safe-root");

        Assert.Throws<InvalidDataException>(() => HubFileSystem.SafeDestination(root, "../outside.txt"));
    }

    private static HubPackageFile PackageFile(string path, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new HubPackageFile(path, Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes.Length);
    }

    private sealed class UpdateFeedFixture : IDisposable
    {
        private const string ManifestUrl = "https://downloads.example/lakona-hub-manifest.json";
        private readonly string currentVersion;
        private readonly string platform;
        private readonly string updateRoot;
        private readonly HttpClient client;

        public UpdateFeedFixture(string currentVersion, string platform, string releaseVersion, string deltaFrom)
        {
            this.currentVersion = currentVersion;
            this.platform = platform;
            updateRoot = Path.Combine(Path.GetTempPath(), $"lakona-hub-feed-{Guid.NewGuid():N}");
            var manifest = new HubReleaseManifest(
                1,
                releaseVersion,
                $"hub-v{releaseVersion}",
                DateTimeOffset.UtcNow,
                "bruce48x/Lakona",
                new Dictionary<string, HubReleasePlatform>
                {
                    [platform] = new(
                        platform.StartsWith("osx", StringComparison.Ordinal) ? "Lakona Hub.app" : "lakona-hub",
                        platform.StartsWith("win", StringComparison.Ordinal) ? "Lakona.Hub.exe" : "Lakona.Hub",
                        new HubReleaseAsset("hub-full.zip", new string('a', 64), 100),
                        [new HubReleaseDelta(deltaFrom, "hub.delta.zip", new string('b', 64), 25)])
                });
            var releases = $$"""
                [{
                  "draft": false,
                  "prerelease": false,
                  "tag_name": "hub-v{{releaseVersion}}",
                  "assets": [{
                    "name": "lakona-hub-manifest.json",
                    "browser_download_url": "{{ManifestUrl}}"
                  }]
                }]
                """;
            client = new HttpClient(new StubHandler(new Dictionary<string, string>
            {
                ["https://api.github.com/repos/bruce48x/Lakona/releases?per_page=30"] = releases,
                [ManifestUrl] = JsonSerializer.Serialize(manifest, HubReleaseManifest.JsonOptions)
            }));
        }

        public HubUpdateService CreateService() => new(client, currentVersion, platform, updateRoot);

        public void Dispose()
        {
            client.Dispose();
            if (Directory.Exists(updateRoot))
            {
                Directory.Delete(updateRoot, recursive: true);
            }
        }
    }

    private sealed class StubHandler(IReadOnlyDictionary<string, string> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is null || !responses.TryGetValue(request.RequestUri.AbsoluteUri, out var content))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            });
        }
    }
}
