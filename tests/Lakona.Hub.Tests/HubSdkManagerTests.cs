using System.IO.Compression;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lakona.Hub.Sdk;
using Xunit;

namespace Lakona.Hub.Tests;

public sealed class HubSdkManagerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "Lakona.Hub.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task InspectAsync_UsesCompatibleSystemSdkWithoutDownloading()
    {
        using var client = new HttpClient(new RejectingHandler());
        var manager = new HubSdkManager(
            client,
            new StubCommandRunner(listOutput: "10.0.102 [C:\\Program Files\\dotnet\\sdk]"),
            "win-x64",
            root);

        var status = await manager.InspectAsync(TestContext.Current.CancellationToken);

        Assert.True(status.IsReady);
        Assert.Equal(HubSdkSource.System, status.Source);
        Assert.Equal("10.0.102", status.Version);
        Assert.Equal("dotnet", status.ExecutablePath);
    }

    [Fact]
    public async Task InspectAsync_RejectsSystemSdkBelowVersion10()
    {
        using var client = new HttpClient(new RejectingHandler());
        var manager = new HubSdkManager(
            client,
            new StubCommandRunner(listOutput: "9.0.301 [C:\\Program Files\\dotnet\\sdk]"),
            "win-x64",
            root);

        var status = await manager.InspectAsync(TestContext.Current.CancellationToken);

        Assert.False(status.IsReady);
        Assert.Equal(HubSdkSource.None, status.Source);
    }

    [Fact]
    public async Task InspectAsync_FindsSystemSdkInStandardMacOsLocationWhenPathIsLimited()
    {
        const string installedPath = "/usr/local/share/dotnet/dotnet";
        using var client = new HttpClient(new RejectingHandler());
        var manager = new HubSdkManager(
            client,
            new PathAwareCommandRunner(installedPath, "10.0.202 [/usr/local/share/dotnet/sdk]"),
            "osx-arm64",
            root);

        var status = await manager.InspectAsync(TestContext.Current.CancellationToken);

        Assert.True(status.IsReady);
        Assert.Equal(installedPath, status.ExecutablePath);
        Assert.Equal("10.0.202", status.Version);
    }

    [Fact]
    public async Task InstallAsync_UsesSelectedAssetPlatformAndAtomicallyActivatesPrivateSdk()
    {
        var archive = CreateSdkZip();
        const string archiveUrl = "https://downloads.example/dotnet-sdk.zip";
        var metadata = JsonSerializer.Serialize(new
        {
            releases = new[]
            {
                new
                {
                    sdk = new
                    {
                        version = "10.0.100",
                        files = new[]
                        {
                            new
                            {
                                name = "dotnet-sdk-10.0.100-win-x64.zip",
                                rid = "win-x64",
                                url = archiveUrl,
                                hash = Convert.ToHexString(SHA512.HashData(archive))
                            }
                        }
                    }
                }
            }
        });
        using var client = new HttpClient(new SdkFeedHandler(metadata, archiveUrl, archive));
        var manager = new HubSdkManager(client, new StubCommandRunner(versionOutput: "10.0.100"), "win-x64", root);
        var progress = new RecordingProgress<HubSdkProgress>();

        var status = await manager.InstallAsync(progress, TestContext.Current.CancellationToken);

        Assert.True(status.IsReady);
        Assert.Equal(HubSdkSource.Managed, status.Source);
        Assert.Equal("10.0.100", status.Version);
        Assert.True(File.Exists(status.ExecutablePath));
        Assert.Contains(progress.Values, value => value.Stage == HubSdkInstallStage.Downloading && value.Percentage == 100);
        Assert.Equal(HubSdkInstallStage.Completed, progress.Values[^1].Stage);
    }

    [Fact]
    public async Task InstallAsync_RejectsArchiveWithInvalidHashWithoutActivation()
    {
        var archive = CreateSdkZip();
        const string archiveUrl = "https://downloads.example/dotnet-sdk.zip";
        var metadata = CreateMetadata(archiveUrl, new string('0', 128));
        using var client = new HttpClient(new SdkFeedHandler(metadata, archiveUrl, archive));
        var manager = new HubSdkManager(client, new StubCommandRunner(versionOutput: "10.0.100"), "win-x64", root);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            manager.InstallAsync(cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("SHA-512", error.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(root, "10.0.100", "win-x64")));
    }

    [Fact]
    public void ReplaceDirectory_RestoresPreviousSdkWhenActivationFails()
    {
        var target = Path.Combine(root, "10.0.100", "win-x64");
        Directory.CreateDirectory(target);
        var previousHost = Path.Combine(target, "dotnet.exe");
        File.WriteAllText(previousHost, "previous SDK");
        var missingReplacement = Path.Combine(root, "missing-replacement");

        Assert.ThrowsAny<IOException>(() => HubSdkActivation.ReplaceDirectory(missingReplacement, target));

        Assert.True(File.Exists(previousHost));
        Assert.Equal("previous SDK", File.ReadAllText(previousHost));
    }

    [Fact]
    public async Task CommandRunner_CancellationTerminatesChildProcess()
    {
        var process = new StubSdkProcess();
        var runner = new HubSdkCommandRunner(new StubSdkProcessFactory(process));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            runner.RunAsync("dotnet", "--version", cancellation.Token));

        Assert.True(process.Killed);
    }

    private static string CreateMetadata(string archiveUrl, string hash) => JsonSerializer.Serialize(new
    {
        releases = new[]
        {
            new
            {
                sdk = new
                {
                    version = "10.0.100",
                    files = new[]
                    {
                        new
                        {
                            name = "dotnet-sdk-10.0.100-win-x64.zip",
                            rid = "win-x64",
                            url = archiveUrl,
                            hash
                        }
                    }
                }
            }
        }
    });

    private static byte[] CreateSdkZip()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("dotnet.exe");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write("test host");
        }

        return stream.ToArray();
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class StubCommandRunner(string listOutput = "", string versionOutput = "") : IHubSdkCommandRunner
    {
        public Task<HubSdkCommandResult> RunAsync(
            string executablePath,
            string arguments,
            CancellationToken cancellationToken) =>
            Task.FromResult(arguments == "--list-sdks"
                ? new HubSdkCommandResult(0, listOutput, "")
                : new HubSdkCommandResult(0, versionOutput, ""));
    }

    private sealed class PathAwareCommandRunner(string installedPath, string listOutput) : IHubSdkCommandRunner
    {
        public Task<HubSdkCommandResult> RunAsync(
            string executablePath,
            string arguments,
            CancellationToken cancellationToken) =>
            Task.FromResult(executablePath == installedPath
                ? new HubSdkCommandResult(0, listOutput, "")
                : new HubSdkCommandResult(-1, "", "not found"));
    }

    private sealed class RecordingProgress<T> : IProgress<T>
    {
        public List<T> Values { get; } = [];

        public void Report(T value) => Values.Add(value);
    }

    private sealed class RejectingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The SDK feed must not be called when a compatible system SDK exists.");
    }

    private sealed class SdkFeedHandler(string metadata, string archiveUrl, byte[] archive) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsoluteUri == HubSdkManager.ReleaseMetadataUrl)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(metadata, Encoding.UTF8, "application/json")
                });
            }

            if (request.RequestUri?.AbsoluteUri == archiveUrl)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(archive)
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class StubSdkProcessFactory(IHubSdkProcess process) : IHubSdkProcessFactory
    {
        public IHubSdkProcess? Start(ProcessStartInfo startInfo) => process;
    }

    private sealed class StubSdkProcess : IHubSdkProcess
    {
        public TextReader StandardOutput { get; } = new StringReader(string.Empty);

        public TextReader StandardError { get; } = new StringReader(string.Empty);

        public int ExitCode => 0;

        public bool HasExited { get; private set; }

        public bool Killed { get; private set; }

        public Task WaitForExitAsync(CancellationToken cancellationToken) => HasExited
            ? Task.CompletedTask
            : Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        public void Kill(bool entireProcessTree)
        {
            Killed = true;
            HasExited = true;
        }

        public void Dispose()
        {
            StandardOutput.Dispose();
            StandardError.Dispose();
        }
    }
}
