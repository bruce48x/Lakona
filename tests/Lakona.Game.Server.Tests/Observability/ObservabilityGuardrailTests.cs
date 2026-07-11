using Lakona.Game.Server.Guardrails;
using Lakona.Game.Server.Guardrails.Rules;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Lakona.Game.Server.Tests.Observability;

public sealed class ObservabilityGuardrailTests
{
    [Fact]
    public void Validate_EmitsError_WhenLocalAdminRequiresLoopbackButSharedListenerBindsNonLoopback()
    {
        var result = Validate(TestRuntime() with
        {
            Observability = TestObservability(
                localAdminEnabled: true,
                localHttpHost: "0.0.0.0",
                localAdminRequireLoopback: true)
        });

        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == "LAKONA130");
        Assert.Equal(LakonaGameDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Lakona:Health:Http:Host", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("127.0.0.1", diagnostic.Repair, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_EmitsWarning_WhenDetailedDiagnosticsAreEnabled()
    {
        var result = Validate(TestRuntime() with
        {
            Observability = TestObservability(detailEnabled: true)
        });

        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == "LAKONA131");
        Assert.Equal(LakonaGameDiagnosticSeverity.Warning, diagnostic.Severity);
    }

    [Fact]
    public void Validate_EmitsError_WhenDetailedDiagnosticsAreExposedThroughNonLoopbackLocalAdmin()
    {
        var result = Validate(TestRuntime() with
        {
            Observability = TestObservability(
                localAdminEnabled: true,
                localHttpHost: "192.168.1.20",
                detailEnabled: true)
        });

        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == "LAKONA132");
        Assert.Equal(LakonaGameDiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void Validate_EmitsError_WhenFileLoggingIsEnabledWithoutCapability()
    {
        var result = Validate(TestRuntime() with
        {
            Observability = TestObservability(fileLoggingEnabled: true)
        });

        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == "LAKONA133");
        Assert.Equal(LakonaGameDiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void Validate_EmitsError_WhenTraceExportIsEnabledWithoutCapability()
    {
        var result = Validate(TestRuntime() with
        {
            Observability = TestObservability(traceExportEnabled: true)
        });

        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == "LAKONA134");
        Assert.Equal(LakonaGameDiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void Validate_EmitsError_WhenPrometheusIsEnabledWithoutCapability()
    {
        var result = Validate(TestRuntime() with
        {
            Observability = TestObservability(prometheusEnabled: true)
        });

        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == "LAKONA135");
        Assert.Equal(LakonaGameDiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Theory]
    [InlineData("")]
    [InlineData("metrics")]
    [InlineData("/")]
    [InlineData("/metrics?format=json")]
    [InlineData("/metrics#debug")]
    public void Validate_EmitsError_WhenPrometheusPathIsInvalid(string path)
    {
        var result = Validate(TestRuntime() with
        {
            Observability = TestObservability(
                prometheusEnabled: true,
                prometheusEndpointRegistered: true,
                prometheusPath: path)
        });

        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == "LAKONA136");
        Assert.Equal(LakonaGameDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Lakona:Observability:Metrics:Prometheus:Path", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("/_lakona/metrics", diagnostic.Repair, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_EmitsError_WhenPrometheusPathIsInvalidEvenWhenPrometheusIsDisabled()
    {
        var result = Validate(TestRuntime() with
        {
            Observability = TestObservability(
                prometheusEnabled: false,
                prometheusPath: "metrics")
        });

        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == "LAKONA136");
        Assert.Equal(LakonaGameDiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_EmitsError_WhenEventBufferCapacityIsNotPositive(int capacity)
    {
        var result = Validate(TestRuntime() with
        {
            Observability = TestObservability(eventBufferCapacity: capacity)
        });

        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == "LAKONA137");
        Assert.Equal(LakonaGameDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Lakona:Observability:Diagnostics:EventBuffer:Capacity", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("positive integer", diagnostic.Repair, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("999999999999999999999999")]
    public void Validate_EmitsError_WhenEventBufferCapacityRawValueIsInvalid(string capacity)
    {
        var result = Validate(TestRuntime() with
        {
            Observability = TestObservability(eventBufferCapacityRaw: capacity)
        });

        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == "LAKONA137");
        Assert.Equal(LakonaGameDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Lakona:Observability:Diagnostics:EventBuffer:Capacity", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("positive integer", diagnostic.Repair, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Verbose")]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmitsError_WhenLoggingMinimumLevelIsInvalid(string minimumLevel)
    {
        var result = Validate(TestRuntime() with
        {
            Observability = TestObservability(
                loggingMinimumLevel: minimumLevel)
        });

        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == "LAKONA138");
        Assert.Equal(LakonaGameDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Lakona:Observability:Logging:MinimumLevel", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("Information", diagnostic.Repair, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void Validate_EmitsError_WhenTraceSampleRateIsOutsideInclusiveRange(double sampleRate)
    {
        var result = Validate(TestRuntime() with
        {
            Observability = TestObservability(traceSampleRate: sampleRate)
        });

        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == "LAKONA139");
        Assert.Equal(LakonaGameDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Lakona:Observability:Tracing:Export:SampleRate", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("0.0 and 1.0", diagnostic.Repair, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("not-a-number")]
    public void Validate_EmitsError_WhenTraceSampleRateRawValueIsInvalid(string sampleRate)
    {
        var result = Validate(TestRuntime() with
        {
            Observability = TestObservability(traceSampleRateRaw: sampleRate)
        });

        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == "LAKONA139");
        Assert.Equal(LakonaGameDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Lakona:Observability:Tracing:Export:SampleRate", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("0.0 and 1.0", diagnostic.Repair, StringComparison.Ordinal);
    }

    private static LakonaGameValidationResult Validate(LakonaGameResolvedRuntime runtime)
    {
        return new LakonaGameRuntimeValidator([new ObservabilityRule()]).Validate(runtime);
    }

    private static LakonaGameResolvedRuntime TestRuntime()
    {
        return new LakonaGameResolvedRuntime(
            NodeId: new LakonaGameResolvedValue<string>("dev-1", LakonaGameValueSource.Configuration, "Lakona:Node:Id"),
            Endpoints: [],
            Cluster: new LakonaGameResolvedCluster(
                AdvertisedEndpoints: new Dictionary<string, string>()),
            ClusterEndpoint: null,
            Hotfix: new LakonaGameResolvedHotfix(
                AssemblyPath: new LakonaGameResolvedValue<string>("Server.Hotfix.dll", LakonaGameValueSource.GeneratedConvention),
                AssemblyFileName: new LakonaGameResolvedValue<string>("Server.Hotfix.dll", LakonaGameValueSource.GeneratedConvention)),
            ReliablePush: new LakonaGameResolvedReliablePush(
                StorageMode: new LakonaGameResolvedValue<string>("InMemory", LakonaGameValueSource.Default),
                PendingLimit: new LakonaGameResolvedValue<int>(256, LakonaGameValueSource.Default),
                ResumeWindowSeconds: new LakonaGameResolvedValue<int>(60, LakonaGameValueSource.Default),
                HasSessionIdentityResolver: true),
            Heartbeat: new LakonaGameResolvedHeartbeat(
                Interval: new LakonaGameResolvedValue<TimeSpan>(
                    TimeSpan.FromSeconds(15),
                    LakonaGameValueSource.Configuration,
                    "Lakona:Heartbeat:Interval"),
                Timeout: new LakonaGameResolvedValue<TimeSpan>(
                    TimeSpan.FromSeconds(45),
                    LakonaGameValueSource.Configuration,
                    "Lakona:Heartbeat:Timeout")),
            Observability: TestObservability());
    }

    private static LakonaGameResolvedObservability TestObservability(
        bool localAdminEnabled = false,
        string localHttpHost = "127.0.0.1",
        bool localAdminRequireLoopback = true,
        bool detailEnabled = false,
        bool fileLoggingEnabled = false,
        bool fileLoggingIntegrationRegistered = false,
        bool traceExportEnabled = false,
        bool openTelemetryIntegrationRegistered = false,
        bool prometheusEnabled = false,
        bool prometheusEndpointRegistered = false,
        string prometheusPath = "/_lakona/metrics",
        int eventBufferCapacity = 1024,
        string eventBufferCapacityRaw = "1024",
        string loggingMinimumLevel = nameof(LogLevel.Information),
        double traceSampleRate = 1.0,
        string traceSampleRateRaw = "1.0")
    {
        return new LakonaGameResolvedObservability(
            LocalAdminEnabled: new LakonaGameResolvedValue<bool>(localAdminEnabled, LakonaGameValueSource.Configuration, "Lakona:Observability:LocalAdmin:Enabled"),
            LocalHttpHost: new LakonaGameResolvedValue<string>(localHttpHost, LakonaGameValueSource.Configuration, "Lakona:Health:Http:Host"),
            LocalAdminRequireLoopback: new LakonaGameResolvedValue<bool>(localAdminRequireLoopback, LakonaGameValueSource.Configuration, "Lakona:Observability:LocalAdmin:RequireLoopback"),
            DetailEnabled: new LakonaGameResolvedValue<bool>(detailEnabled, LakonaGameValueSource.Configuration, "Lakona:Observability:Diagnostics:DetailEnabled"),
            FileLoggingEnabled: new LakonaGameResolvedValue<bool>(fileLoggingEnabled, LakonaGameValueSource.Configuration, "Lakona:Observability:Logging:File:Enabled"),
            FileLoggingIntegrationRegistered: fileLoggingIntegrationRegistered,
            TraceExportEnabled: new LakonaGameResolvedValue<bool>(traceExportEnabled, LakonaGameValueSource.Configuration, "Lakona:Observability:Tracing:Export:Enabled"),
            OpenTelemetryIntegrationRegistered: openTelemetryIntegrationRegistered,
            PrometheusEnabled: new LakonaGameResolvedValue<bool>(prometheusEnabled, LakonaGameValueSource.Configuration, "Lakona:Observability:Metrics:Prometheus:Enabled"),
            PrometheusEndpointRegistered: prometheusEndpointRegistered,
            PrometheusPath: new LakonaGameResolvedValue<string>(prometheusPath, LakonaGameValueSource.Configuration, "Lakona:Observability:Metrics:Prometheus:Path"),
            EventBufferCapacity: new LakonaGameResolvedValue<int>(eventBufferCapacity, LakonaGameValueSource.Configuration, "Lakona:Observability:Diagnostics:EventBuffer:Capacity"),
            EventBufferCapacityRaw: new LakonaGameResolvedValue<string>(eventBufferCapacityRaw, LakonaGameValueSource.Configuration, "Lakona:Observability:Diagnostics:EventBuffer:Capacity"),
            LoggingMinimumLevel: new LakonaGameResolvedValue<string>(loggingMinimumLevel, LakonaGameValueSource.Configuration, "Lakona:Observability:Logging:MinimumLevel"),
            TraceSampleRate: new LakonaGameResolvedValue<double>(traceSampleRate, LakonaGameValueSource.Configuration, "Lakona:Observability:Tracing:Export:SampleRate"),
            TraceSampleRateRaw: new LakonaGameResolvedValue<string>(traceSampleRateRaw, LakonaGameValueSource.Configuration, "Lakona:Observability:Tracing:Export:SampleRate"));
    }
}
