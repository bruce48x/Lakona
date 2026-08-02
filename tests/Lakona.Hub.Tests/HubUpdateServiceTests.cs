using System.Diagnostics;
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
    public async Task CheckAsync_UsesWindowsInstallerInsteadOfPortableDelta()
    {
        using var fixture = new UpdateFeedFixture("1.2.0", "win-x64", "1.3.0", deltaFrom: "1.2.0");
        var service = fixture.CreateService();

        var update = await service.CheckAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(update);
        Assert.Equal("1.3.0", update.Version);
        Assert.Equal("hub-full.msi", update.Asset.AssetName);
    }

    [Fact]
    public async Task CheckAsync_FallsBackToFullPackageWithoutMatchingDelta()
    {
        using var fixture = new UpdateFeedFixture("1.1.0", "linux-x64-deb", "1.3.0", deltaFrom: "1.1.0");
        var service = fixture.CreateService();

        var update = await service.CheckAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(update);
        Assert.Equal("hub-full.deb", update.Asset.AssetName);
    }

    [Theory]
    [InlineData("ID=ubuntu\nID_LIKE=debian", "deb")]
    [InlineData("ID=rocky\nID_LIKE=\"rhel centos fedora\"", "rpm")]
    [InlineData("ID=opensuse-tumbleweed\nID_LIKE=\"opensuse suse\"", "rpm")]
    public void LinuxPackageFormat_DetectsDistributionFamily(string osRelease, string expected)
    {
        Assert.Equal(expected, LinuxPackageFormat.Detect(osRelease, _ => false));
    }

    [Fact]
    public void LinuxPackageFormat_FallsBackToInstalledPackageManager()
    {
        Assert.Equal("deb", LinuxPackageFormat.Detect("ID=unknown", path => path.EndsWith("dpkg", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task PrepareAndLaunchAsync_OpensVerifiedSystemPackageWithoutReplacingInstallation()
    {
        var package = Encoding.UTF8.GetBytes("native package");
        const string assetName = "lakona-hub-2.0.0-linux-x64.deb";
        const string tag = "hub-v2.0.0";
        var asset = new HubReleaseAsset(
            assetName,
            Convert.ToHexStringLower(SHA256.HashData(package)),
            package.Length);
        var url = $"https://github.com/bruce48x/Lakona/releases/download/{tag}/{assetName}";
        var root = Path.Combine(Path.GetTempPath(), $"lakona-hub-native-update-{Guid.NewGuid():N}");
        var launcher = new RecordingPackageLauncher();
        using var client = new HttpClient(new ByteArrayHandler(url, package));
        try
        {
            var service = new HubUpdateService(client, "1.0.0", "linux-x64-deb", root, launcher);
            var update = new HubAvailableUpdate(
                "2.0.0", "linux-x64-deb", tag, asset);
            var progress = new RecordingProgress<HubUpdateProgress>();

            var result = await service.PrepareAndLaunchAsync(
                update,
                progress,
                TestContext.Current.CancellationToken);

            Assert.Equal(HubUpdateLaunchResult.ApplicationRestartInitiated, result);
            Assert.NotNull(launcher.PackagePath);
            Assert.Equal(assetName, Path.GetFileName(launcher.PackagePath));
            Assert.Equal(package, await File.ReadAllBytesAsync(launcher.PackagePath, TestContext.Current.CancellationToken));
            Assert.Contains(progress.Values, value =>
                value.Stage == HubUpdateStage.Downloading && value.BytesReceived == 0 && value.TotalBytes == package.Length);
            Assert.Contains(progress.Values, value =>
                value.Stage == HubUpdateStage.Downloading && value.BytesReceived == package.Length && value.Percentage == 100);
            Assert.Equal(HubUpdateStage.Installing, progress.Values[^1].Stage);
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
    public void LinuxPackageInstaller_UsesAptGetThroughPolicyKitForDebPackage()
    {
        var packagePath = Path.Combine(Path.GetTempPath(), "lakona hub.deb");
        var existingPaths = new HashSet<string>(StringComparer.Ordinal)
        {
            "/usr/bin/pkexec",
            "/usr/bin/apt-get"
        };

        var startInfo = LinuxPackageInstaller.CreateStartInfo(packagePath, existingPaths.Contains);

        Assert.Equal("/usr/bin/pkexec", startInfo.FileName);
        Assert.False(startInfo.UseShellExecute);
        Assert.Equal(
            ["/usr/bin/apt-get", "install", "--yes", Path.GetFullPath(packagePath)],
            startInfo.ArgumentList);
    }

    [Fact]
    public void LinuxPackageInstaller_UsesAvailableRpmPackageManager()
    {
        var packagePath = Path.Combine(Path.GetTempPath(), "lakona-hub.rpm");
        var existingPaths = new HashSet<string>(StringComparer.Ordinal)
        {
            "/usr/bin/pkexec",
            "/usr/bin/dnf"
        };

        var startInfo = LinuxPackageInstaller.CreateStartInfo(packagePath, existingPaths.Contains);

        Assert.Equal(
            ["/usr/bin/dnf", "install", "--assumeyes", Path.GetFullPath(packagePath)],
            startInfo.ArgumentList);
    }

    [Fact]
    public void LinuxPackageInstaller_RejectsMissingPolicyKit()
    {
        var exception = Assert.Throws<PlatformNotSupportedException>(() =>
            LinuxPackageInstaller.CreateStartInfo("/tmp/lakona-hub.deb", _ => false));

        Assert.Contains("PolicyKit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LinuxPackageInstaller_StartsInstalledHubAsDesktopApplication()
    {
        var startInfo = LinuxPackageInstaller.CreateInstalledHubStartInfo();

        Assert.Equal("/usr/bin/lakona-hub", startInfo.FileName);
        Assert.True(startInfo.UseShellExecute);
        Assert.Equal(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            startInfo.WorkingDirectory);
    }

    [Fact]
    public void WindowsUpdateHandoff_CopiesWorkerAndStartsIt()
    {
        var processFactory = new RecordingUpdateProcessFactory();
        var copies = new List<(string Source, string Destination)>();
        var executablePath = Path.GetFullPath(Path.Combine("installed", "Lakona.Hub.exe"));
        var packagePath = Path.GetFullPath(Path.Combine("updates", "lakona-hub-2.0.0-win-x64.msi"));
        var handoff = new WindowsUpdateHandoff(
            processFactory,
            42,
            executablePath,
            (source, destination) => copies.Add((source, destination)));

        handoff.Start(packagePath);

        var copy = Assert.Single(copies);
        Assert.Equal(executablePath, copy.Source);
        Assert.Equal(Path.Combine(Path.GetDirectoryName(packagePath)!, "Lakona.Hub.UpdateWorker.exe"), copy.Destination);
        var startInfo = Assert.Single(processFactory.StartInfos);
        Assert.Equal(copy.Destination, startInfo.FileName);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.CreateNoWindow);
        Assert.Equal(
            [WindowsUpdateWorker.Command, "42", packagePath, executablePath],
            startInfo.ArgumentList);
    }

    [Fact]
    public async Task WindowsPackageLauncher_RestartsUpdatedApplication()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var processFactory = new RecordingUpdateProcessFactory();
        var executablePath = Path.GetFullPath(Path.Combine("installed", "Lakona.Hub.exe"));
        var packagePath = Path.GetFullPath(Path.Combine("updates", "lakona-hub-2.0.0-win-x64.msi"));
        var handoff = new WindowsUpdateHandoff(
            processFactory,
            42,
            executablePath,
            (_, _) => { });
        var launcher = new HubSystemPackageLauncher(handoff);

        var result = await launcher.OpenAsync(packagePath, TestContext.Current.CancellationToken);

        Assert.Equal(HubUpdateLaunchResult.ApplicationRestartInitiated, result);
        Assert.Single(processFactory.StartInfos);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3010)]
    public async Task WindowsUpdateWorker_WaitsForHubInstallsMsiAndRestartsApplication(int installerExitCode)
    {
        var processFactory = new RecordingUpdateProcessFactory(installerExitCode);
        var packagePath = Path.GetFullPath(Path.Combine("updates", "lakona hub.msi"));
        var executablePath = Path.GetFullPath(Path.Combine("installed", "Lakona.Hub.exe"));
        var request = new WindowsUpdateWorkerRequest(42, packagePath, executablePath);

        var exitCode = await WindowsUpdateWorker.RunAsync(
            request,
            processFactory,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.Equal([42], processFactory.WaitedProcessIds);
        Assert.Equal(2, processFactory.StartInfos.Count);
        var installer = processFactory.StartInfos[0];
        Assert.Equal("msiexec.exe", installer.FileName);
        Assert.True(installer.UseShellExecute);
        Assert.Equal("runas", installer.Verb);
        Assert.Equal(
            ["/i", packagePath, "/passive", "/norestart", "/log", packagePath + ".install.log"],
            installer.ArgumentList);
        Assert.Equal(executablePath, processFactory.StartInfos[1].FileName);
    }

    [Fact]
    public async Task WindowsUpdateWorker_RestartsExistingApplicationWhenInstallationFails()
    {
        var processFactory = new RecordingUpdateProcessFactory(installerExitCode: 1602);
        var request = new WindowsUpdateWorkerRequest(
            42,
            Path.GetFullPath(Path.Combine("updates", "lakona-hub.msi")),
            Path.GetFullPath(Path.Combine("installed", "Lakona.Hub.exe")));

        var exitCode = await WindowsUpdateWorker.RunAsync(
            request,
            processFactory,
            TestContext.Current.CancellationToken);

        Assert.Equal(1602, exitCode);
        Assert.Equal(2, processFactory.StartInfos.Count);
        Assert.Equal(request.InstalledApplicationPath, processFactory.StartInfos[1].FileName);
    }

    [Fact]
    public void WindowsUpdateWorker_ParsesHandoffArguments()
    {
        var packagePath = Path.GetFullPath(Path.Combine("updates", "lakona-hub.msi"));
        var executablePath = Path.GetFullPath(Path.Combine("installed", "Lakona.Hub.exe"));

        var parsed = WindowsUpdateWorker.TryParse(
            [WindowsUpdateWorker.Command, "42", packagePath, executablePath],
            out var request);

        Assert.True(parsed);
        Assert.Equal(new WindowsUpdateWorkerRequest(42, packagePath, executablePath), request);
    }

    [Fact]
    public async Task CheckAsync_ReturnsNullWhenInstalledVersionIsCurrent()
    {
        using var fixture = new UpdateFeedFixture("1.3.0", "osx-arm64", "1.3.0", deltaFrom: "1.2.0");

        var update = await fixture.CreateService().CheckAsync(TestContext.Current.CancellationToken);

        Assert.Null(update);
    }

    [Fact]
    public async Task CheckAsync_SelectsHighestVersionWhenGitHubReleasesAreNotVersionOrdered()
    {
        const string olderManifestUrl = "https://downloads.example/lakona-hub-manifest-0.2.8.json";
        const string newerManifestUrl = "https://downloads.example/lakona-hub-manifest-0.2.12.json";
        var releases = $$"""
            [{
              "draft": false,
              "prerelease": false,
              "tag_name": "hub-v0.2.8",
              "assets": [{
                "name": "lakona-hub-manifest.json",
                "browser_download_url": "{{olderManifestUrl}}"
              }]
            }, {
              "draft": false,
              "prerelease": false,
              "tag_name": "hub-v0.2.12",
              "assets": [{
                "name": "lakona-hub-manifest.json",
                "browser_download_url": "{{newerManifestUrl}}"
              }]
            }]
            """;
        using var client = new HttpClient(new StubHandler(new Dictionary<string, string>
        {
            ["https://api.github.com/repos/bruce48x/Lakona/releases?per_page=30"] = releases,
            [olderManifestUrl] = SerializeManifest("0.2.8", "win-x64"),
            [newerManifestUrl] = SerializeManifest("0.2.12", "win-x64")
        }));
        var service = new HubUpdateService(client, "0.2.8", "win-x64", Path.GetTempPath());

        var update = await service.CheckAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(update);
        Assert.Equal("0.2.12", update.Version);
    }

    private static string SerializeManifest(string version, string platform)
    {
        var manifest = new HubReleaseManifest(
            1,
            version,
            $"hub-v{version}",
            DateTimeOffset.UtcNow,
            "bruce48x/Lakona",
            new Dictionary<string, HubReleasePlatform>
            {
                [platform] = new(
                    "lakona-hub",
                    "Lakona.Hub.exe",
                    new HubReleaseAsset("hub-full.msi", new string('a', 64), 100),
                    [])
            });
        return JsonSerializer.Serialize(manifest, HubReleaseManifest.JsonOptions);
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
                        new HubReleaseAsset(
                            FullAssetName(platform),
                            new string('a', 64),
                            100),
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

        private static string FullAssetName(string platform)
        {
            if (platform.StartsWith("win-", StringComparison.Ordinal))
            {
                return "hub-full.msi";
            }

            if (platform.StartsWith("osx-", StringComparison.Ordinal))
            {
                return "hub-full.dmg";
            }

            return platform.EndsWith("-deb", StringComparison.Ordinal) ? "hub-full.deb" : "hub-full.rpm";
        }

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

    private sealed class ByteArrayHandler(string url, byte[] content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(request.RequestUri?.AbsoluteUri == url
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private sealed class RecordingPackageLauncher : IHubSystemPackageLauncher
    {
        public string? PackagePath { get; private set; }

        public Task<HubUpdateLaunchResult> OpenAsync(string packagePath, CancellationToken cancellationToken)
        {
            PackagePath = packagePath;
            return Task.FromResult(HubUpdateLaunchResult.ApplicationRestartInitiated);
        }
    }

    private sealed class RecordingUpdateProcessFactory(int installerExitCode = 0) : IHubUpdateProcessFactory
    {
        public List<ProcessStartInfo> StartInfos { get; } = [];

        public List<int> WaitedProcessIds { get; } = [];

        public IHubUpdateProcess? Start(ProcessStartInfo startInfo)
        {
            StartInfos.Add(startInfo);
            return new StubUpdateProcess(StartInfos.Count == 1 ? installerExitCode : 0);
        }

        public Task WaitForExitAsync(int processId, CancellationToken cancellationToken)
        {
            WaitedProcessIds.Add(processId);
            return Task.CompletedTask;
        }
    }

    private sealed class StubUpdateProcess(int exitCode) : IHubUpdateProcess
    {
        public int ExitCode { get; } = exitCode;

        public Task WaitForExitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class RecordingProgress<T> : IProgress<T>
    {
        public List<T> Values { get; } = [];

        public void Report(T value) => Values.Add(value);
    }
}
