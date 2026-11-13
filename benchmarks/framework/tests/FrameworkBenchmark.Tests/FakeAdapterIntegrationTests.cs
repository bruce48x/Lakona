using System.Diagnostics;
using FrameworkBenchmark.Contracts;
using FrameworkBenchmark.Coordinator;
using Xunit;

namespace FrameworkBenchmark.Tests;

public sealed class FakeAdapterIntegrationTests
{
    [Fact]
    public async Task RunAsync_FakeAdapterProducesValidatedBundle()
    {
        using var fixture = new TemporaryFixture();
        var suitePath = fixture.WriteSuite();
        var manifestPath = fixture.WriteManifest("normal", "normal");
        var coordinator = new BenchmarkCoordinator();

        var summary = await coordinator.RunAsync(
            new CoordinatorOptions(suitePath, [manifestPath], fixture.OutputRoot),
            TestContext.Current.CancellationToken);

        var result = Assert.Single(summary.Cases);
        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
        Assert.Equal("fixture-revision", result.Result.Metadata["adapterRevision"]);
        var runDirectory = Assert.Single(Directory.GetDirectories(fixture.OutputRoot));
        Assert.True(File.Exists(Path.Combine(runDirectory, "run-manifest.json")));
        Assert.Equal(
            "complete",
            BenchmarkJson.Read<RunManifest>(Path.Combine(runDirectory, "run-manifest.json")).Status);
        Assert.True(File.Exists(Path.Combine(runDirectory, "summary.json")));
        Assert.True(File.Exists(Path.Combine(runDirectory, "validation.json")));
        Assert.True(File.Exists(Path.Combine(runDirectory, "report.md")));
        Assert.NotEmpty(summary.Environment);
        var report = File.ReadAllText(Path.Combine(runDirectory, "report.md"));
        Assert.Contains("No aggregate score", report, StringComparison.Ordinal);
        Assert.Contains("## Rerun", report, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(runDirectory, "fake-prepare.pid")));
        Assert.True(File.Exists(Path.Combine(runDirectory, "fake-shutdown.pid")));
        Assert.Single(Directory.GetFiles(Path.Combine(runDirectory, "histograms"), "*.json"));
    }

    [Theory]
    [InlineData("exit-before-ready")]
    [InlineData("malformed-ready")]
    [InlineData("never-ready")]
    [InlineData("duplicate-ready")]
    public async Task RunAsync_ServerReadinessFailureStopsProcess(string behavior)
    {
        using var fixture = new TemporaryFixture();
        var suitePath = fixture.WriteSuite();
        var manifestPath = fixture.WriteManifest(behavior, "normal");
        var coordinator = new BenchmarkCoordinator();

        await Assert.ThrowsAsync<BenchmarkToolException>(() => coordinator.RunAsync(
            new CoordinatorOptions(suitePath, [manifestPath], fixture.OutputRoot),
            TestContext.Current.CancellationToken));

        var runDirectory = Assert.Single(Directory.GetDirectories(fixture.OutputRoot));
        Assert.Equal(
            "incomplete",
            BenchmarkJson.Read<RunManifest>(Path.Combine(runDirectory, "run-manifest.json")).Status);
        var pidFile = Directory.GetFiles(fixture.OutputRoot, "fake-server.pid", SearchOption.AllDirectories).Single();
        var pid = int.Parse(File.ReadAllText(pidFile), System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(ProcessHasExited(pid), $"Fake server process {pid} was left running.");
        Assert.True(File.Exists(Path.Combine(runDirectory, "fake-shutdown.pid")));
    }

    [Fact]
    public async Task RunAsync_CorruptDriverResultCompletesAsInvalidCase()
    {
        using var fixture = new TemporaryFixture();
        var coordinator = new BenchmarkCoordinator();

        var summary = await coordinator.RunAsync(
            new CoordinatorOptions(
                fixture.WriteSuite(),
                [fixture.WriteManifest("normal", "corrupt-result")],
                fixture.OutputRoot),
            TestContext.Current.CancellationToken);

        var result = Assert.Single(summary.Cases);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, static error => error.Contains("caseId", StringComparison.Ordinal));
        var runDirectory = Assert.Single(Directory.GetDirectories(fixture.OutputRoot));
        Assert.Equal(
            "complete",
            BenchmarkJson.Read<RunManifest>(Path.Combine(runDirectory, "run-manifest.json")).Status);
    }

    [Fact]
    public async Task RunAsync_CancellationStopsServerAndDriver()
    {
        using var fixture = new TemporaryFixture();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var coordinator = new BenchmarkCoordinator();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.RunAsync(
            new CoordinatorOptions(
                fixture.WriteSuite(),
                [fixture.WriteManifest("normal", "never-completes")],
                fixture.OutputRoot),
            cancellation.Token));

        foreach (var name in new[] { "fake-server.pid", "fake-driver.pid" })
        {
            var pidFile = Directory.GetFiles(fixture.OutputRoot, name, SearchOption.AllDirectories).Single();
            var pid = int.Parse(File.ReadAllText(pidFile), System.Globalization.CultureInfo.InvariantCulture);
            Assert.True(ProcessHasExited(pid), $"Process {pid} from {name} was left running.");
        }

        var runDirectory = Assert.Single(Directory.GetDirectories(fixture.OutputRoot));
        Assert.Equal(
            "incomplete",
            BenchmarkJson.Read<RunManifest>(Path.Combine(runDirectory, "run-manifest.json")).Status);
        Assert.True(File.Exists(Path.Combine(runDirectory, "fake-shutdown.pid")));
    }

    [Fact]
    public async Task RunAsync_ShutdownFailureStillStopsServer()
    {
        using var fixture = new TemporaryFixture();
        await Assert.ThrowsAsync<AggregateException>(() => new BenchmarkCoordinator().RunAsync(
            new CoordinatorOptions(fixture.WriteSuite(), [fixture.WriteManifest("normal", "normal", "shutdown-failure")], fixture.OutputRoot),
            TestContext.Current.CancellationToken));
        var runDirectory = Assert.Single(Directory.GetDirectories(fixture.OutputRoot));
        var pid = int.Parse(File.ReadAllText(Path.Combine(runDirectory, "fake-server.pid")), System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(ProcessHasExited(pid));
        Assert.True(File.Exists(Path.Combine(runDirectory, "fake-shutdown.pid")));
        Assert.Equal("incomplete", BenchmarkJson.Read<RunManifest>(Path.Combine(runDirectory, "run-manifest.json")).Status);
    }

    [Theory]
    [InlineData(1000, 30000, "startup budget")]
    [InlineData(30000, 1000, "did not become ready")]
    public async Task StartupAndReadinessBudgetsAreBothEnforced(int startup, int readiness, string expected)
    {
        using var fixture = new TemporaryFixture();
        var timing = SuiteExpanderTests.CreateSuite().Timing with
        {
            StartupTimeoutMilliseconds = startup, ReadinessTimeoutMilliseconds = readiness,
            ShutdownTimeoutMilliseconds = 5000
        };
        var error = await Assert.ThrowsAsync<BenchmarkToolException>(() => new BenchmarkCoordinator().RunAsync(
            new CoordinatorOptions(fixture.WriteSuite(timing), [fixture.WriteManifest("never-ready", "normal")], fixture.OutputRoot),
            TestContext.Current.CancellationToken));
        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
        AssertOwnedProcessesStopped(fixture.OutputRoot);
    }

    [Fact]
    public async Task StartupBudgetIsSharedAcrossServers()
    {
        using var fixture = new TemporaryFixture();
        var manifestPath = fixture.WriteManifest("slow-ready", "normal");
        var manifest = BenchmarkJson.Read<AdapterManifest>(manifestPath);
        var first = manifest.Servers[0];
        var second = first with
        {
            Role = "second",
            Ports = ["secondPort"],
            Command = first.Command with
            {
                Arguments = first.Command.Arguments.Select(value => value == "frontdoor" ? "second" :
                    value.Replace("fake-server.pid", "second-server.pid", StringComparison.Ordinal)
                        .Replace("${clientPort}", "${secondPort}", StringComparison.Ordinal)).ToArray()
            }
        };
        BenchmarkJson.Write(manifestPath, manifest with { Servers = [first, second] });
        var timing = SuiteExpanderTests.CreateSuite().Timing with
        {
            StartupTimeoutMilliseconds = 5000, ReadinessTimeoutMilliseconds = 30000,
            ShutdownTimeoutMilliseconds = 5000
        };
        var error = await Assert.ThrowsAsync<BenchmarkToolException>(() => new BenchmarkCoordinator().RunAsync(
            new CoordinatorOptions(fixture.WriteSuite(timing), [manifestPath], fixture.OutputRoot),
            TestContext.Current.CancellationToken));
        Assert.Contains("startup budget", error.Message, StringComparison.Ordinal);
        Assert.Contains("second", error.Message, StringComparison.Ordinal);
        AssertOwnedProcessesStopped(fixture.OutputRoot);
    }

    [Fact]
    public async Task ShutdownBudgetStopsSlowCommandAndStillReclaimsServer()
    {
        using var fixture = new TemporaryFixture();
        var watch = Stopwatch.StartNew();
        var error = await Assert.ThrowsAsync<AggregateException>(() => new BenchmarkCoordinator().RunAsync(
            new CoordinatorOptions(fixture.WriteSuite(), [fixture.WriteManifest("normal", "normal", "shutdown-hang")], fixture.OutputRoot),
            TestContext.Current.CancellationToken));
        Assert.Contains("exceeded timeout 00:00:01", error.ToString(), StringComparison.Ordinal);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(12), error.ToString());
        AssertOwnedProcessesStopped(fixture.OutputRoot);
    }

    private static void AssertOwnedProcessesStopped(string root)
    {
        foreach (var path in Directory.GetFiles(root, "*.pid", SearchOption.AllDirectories))
            Assert.True(ProcessHasExited(int.Parse(File.ReadAllText(path), System.Globalization.CultureInfo.InvariantCulture)), path);
        var runDirectory = Assert.Single(Directory.GetDirectories(root));
        Assert.Equal("incomplete", BenchmarkJson.Read<RunManifest>(Path.Combine(runDirectory, "run-manifest.json")).Status);
    }

    private static bool ProcessHasExited(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.HasExited;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private sealed class TemporaryFixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), $"framework-benchmark-tests-{Guid.NewGuid():N}");

        public TemporaryFixture()
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(OutputRoot);
        }

        public string OutputRoot => Path.Combine(root, "output");

        public string WriteSuite(SuiteTiming? timing = null)
        {
            var path = Path.Combine(root, "suite.json");
            var suite = SuiteExpanderTests.CreateSuite();
            BenchmarkJson.Write(path, timing is null ? suite : suite with { Timing = timing });
            return path;
        }

        public string WriteManifest(string serverBehavior, string driverBehavior, string shutdownBehavior = "normal")
        {
            var script = TestPaths.Fixture("fake-adapter.ps1");
            var serverCommand = new ProcessCommand(
                "pwsh",
                [
                    "-NoProfile",
                    "-File",
                    script,
                    "-Mode",
                    "server",
                    "-Role",
                    "frontdoor",
                    "-Port",
                    "${clientPort}",
                    "-PidFile",
                    "${runDir}/fake-server.pid"
                ],
                new Dictionary<string, string> { ["FAKE_BENCHMARK_BEHAVIOR"] = serverBehavior });
            var driverCommand = new ProcessCommand(
                "pwsh",
                [
                    "-NoProfile",
                    "-File",
                    script,
                    "-Mode",
                    "driver",
                    "-CaseFile",
                    "${caseFile}",
                    "-ResultFile",
                    "${resultFile}",
                    "-PidFile",
                    "${runDir}/fake-driver.pid"
                ],
                new Dictionary<string, string> { ["FAKE_BENCHMARK_BEHAVIOR"] = driverBehavior });
            var manifest = new AdapterManifest(
                BenchmarkSchemaVersions.V1,
                "fake",
                "fixture-revision",
                "PowerShell 7",
                "test",
                "https://example.invalid/license",
                ["frontdoor.echo"],
                new Dictionary<string, string> { ["fixture"] = "true" },
                [new ProcessCommand(
                    "pwsh",
                    [
                        "-NoProfile",
                        "-File",
                        script,
                        "-Mode",
                        "prepare",
                        "-PidFile",
                        "${runDir}/fake-prepare.pid"
                    ])],
                [new ServerProcessSpecification("frontdoor", ["clientPort"], serverCommand,
                    new ProcessCommand("pwsh", ["-NoProfile", "-File", script, "-Mode", "shutdown", "-PidFile", "${runDir}/fake-shutdown.pid"],
                        new Dictionary<string, string> { ["FAKE_BENCHMARK_BEHAVIOR"] = shutdownBehavior }))],
                driverCommand);
            var path = Path.Combine(root, "adapter.json");
            BenchmarkJson.Write(path, manifest);
            return path;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
