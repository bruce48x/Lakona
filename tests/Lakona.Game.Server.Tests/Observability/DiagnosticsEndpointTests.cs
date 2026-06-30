using System.Text.Json;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Loading;
using Lakona.Game.Server.LocalAdmin;
using Lakona.Game.Server.Observability;
using Lakona.Game.Server.Observability.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Lakona.Game.Server.Tests.Observability;

public sealed class DiagnosticsEndpointTests
{
    [Fact]
    public async Task Summary_endpoint_returns_bounded_safe_json()
    {
        var sink = new BoundedDiagnosticsEventBuffer(8, LogLevel.Trace);
        var routes = DiagnosticsLocalAdminRoutes.Create(
            new LakonaDiagnosticsSnapshotService(
            [
                new TestSnapshotProvider("actors", new { actorTypes = Array.Empty<object>() }),
                new TestSnapshotProvider("sessions", new { totalSessions = 0 }),
                new TestSnapshotProvider("process", new { processId = 123, uptimeSeconds = 1 })
            ],
            sink),
            sink);
        var router = new LakonaLocalAdminRouter(routes);

        var response = await router.RouteAsync(
            new LakonaLocalAdminRequest("GET", "/_lakona/diagnostics/summary", Stream.Null, true),
            TestContext.Current.CancellationToken);

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("application/json", response.ContentType);
        Assert.DoesNotContain("secret-token", response.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-session", response.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-actor", response.Body, StringComparison.OrdinalIgnoreCase);
        Assert.True(response.Body.Length < 16_384);

        using var document = JsonDocument.Parse(response.Body);
        Assert.Equal("ok", document.RootElement.GetProperty("status").GetString());
        Assert.True(document.RootElement.GetProperty("sections").TryGetProperty("actors", out _));
        Assert.True(document.RootElement.GetProperty("sections").TryGetProperty("sessions", out _));
        Assert.Empty(document.RootElement.GetProperty("errors").EnumerateArray());
    }

    [Fact]
    public async Task Events_endpoint_returns_recent_events()
    {
        var sink = new BoundedDiagnosticsEventBuffer(8, LogLevel.Trace);
        sink.Publish(new DiagnosticsEvent(
            DateTimeOffset.UtcNow,
            LogLevel.Warning,
            "Lakona.Game.Tests",
            "test.event",
            "recent event",
            TraceId: null,
            CorrelationId: null,
            new Dictionary<string, string?> { ["provider"] = "test" }));
        var router = new LakonaLocalAdminRouter(DiagnosticsLocalAdminRoutes.Create(
            new LakonaDiagnosticsSnapshotService([], sink),
            sink));

        var response = await router.RouteAsync(
            new LakonaLocalAdminRequest("GET", "/_lakona/diagnostics/events", Stream.Null, true),
            TestContext.Current.CancellationToken);

        Assert.Equal(200, response.StatusCode);
        using var document = JsonDocument.Parse(response.Body);
        var evt = Assert.Single(document.RootElement.GetProperty("events").EnumerateArray());
        Assert.Equal("test.event", evt.GetProperty("kind").GetString());
        Assert.Equal("recent event", evt.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Provider_failure_returns_partial_summary_and_records_event()
    {
        var sink = new BoundedDiagnosticsEventBuffer(8, LogLevel.Trace);
        var snapshots = new LakonaDiagnosticsSnapshotService(
        [
            new TestSnapshotProvider("process", new { processId = 123 }),
            new ThrowingSnapshotProvider("broken")
        ],
        sink);
        var router = new LakonaLocalAdminRouter(DiagnosticsLocalAdminRoutes.Create(snapshots, sink));

        var response = await router.RouteAsync(
            new LakonaLocalAdminRequest("GET", "/_lakona/diagnostics/summary", Stream.Null, true),
            TestContext.Current.CancellationToken);

        Assert.Equal(200, response.StatusCode);
        Assert.DoesNotContain("secret-token", response.Body, StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(response.Body);
        Assert.Equal("partial", document.RootElement.GetProperty("status").GetString());
        var error = Assert.Single(document.RootElement.GetProperty("errors").EnumerateArray());
        Assert.Equal("broken", error.GetProperty("provider").GetString());
        Assert.Equal("InvalidOperationException", error.GetProperty("errorType").GetString());

        var diagnostic = Assert.Single(sink.Snapshot(10));
        Assert.Equal("diagnostics.provider.failure", diagnostic.Kind);
        Assert.Equal(LogLevel.Error, diagnostic.Level);
        Assert.Equal("broken", diagnostic.Dimensions["provider"]);
        Assert.DoesNotContain("secret-token", diagnostic.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Netstat_endpoint_returns_unavailable_status()
    {
        var sink = new BoundedDiagnosticsEventBuffer(8, LogLevel.Trace);
        var router = new LakonaLocalAdminRouter(DiagnosticsLocalAdminRoutes.Create(
            new LakonaDiagnosticsSnapshotService([], sink),
            sink));

        var response = await router.RouteAsync(
            new LakonaLocalAdminRequest("GET", "/_lakona/diagnostics/netstat", Stream.Null, true),
            TestContext.Current.CancellationToken);

        Assert.Equal(200, response.StatusCode);
        using var document = JsonDocument.Parse(response.Body);
        Assert.Equal("unavailable", document.RootElement.GetProperty("status").GetString());
        Assert.NotEqual("ok", document.RootElement.GetProperty("status").GetString());
        Assert.Contains("deferred", document.RootElement.GetProperty("explanation").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Actor_and_session_endpoints_return_safe_empty_shape_when_sections_are_missing()
    {
        var sink = new BoundedDiagnosticsEventBuffer(8, LogLevel.Trace);
        var router = new LakonaLocalAdminRouter(DiagnosticsLocalAdminRoutes.Create(
            new LakonaDiagnosticsSnapshotService([], sink),
            sink));

        var actors = await router.RouteAsync(
            new LakonaLocalAdminRequest("GET", "/_lakona/diagnostics/actors", Stream.Null, true),
            TestContext.Current.CancellationToken);
        var sessions = await router.RouteAsync(
            new LakonaLocalAdminRequest("GET", "/_lakona/diagnostics/sessions", Stream.Null, true),
            TestContext.Current.CancellationToken);

        using var actorsDocument = JsonDocument.Parse(actors.Body);
        using var sessionsDocument = JsonDocument.Parse(sessions.Body);
        Assert.Empty(actorsDocument.RootElement.GetProperty("actorTypes").EnumerateArray());
        Assert.Equal(0, sessionsDocument.RootElement.GetProperty("totalSessions").GetInt32());
    }

    [Theory]
    [InlineData("/_lakona/diagnostics/actors", "actors", "actorTypes")]
    [InlineData("/_lakona/diagnostics/sessions", "sessions", "totalSessions")]
    public async Task Section_endpoint_provider_failure_returns_partial_envelope(
        string path,
        string providerName,
        string emptyShapeProperty)
    {
        var sink = new BoundedDiagnosticsEventBuffer(8, LogLevel.Trace);
        var snapshots = new LakonaDiagnosticsSnapshotService(
        [
            new ThrowingSnapshotProvider(providerName)
        ],
        sink);
        var router = new LakonaLocalAdminRouter(DiagnosticsLocalAdminRoutes.Create(snapshots, sink));

        var response = await router.RouteAsync(
            new LakonaLocalAdminRequest("GET", path, Stream.Null, true),
            TestContext.Current.CancellationToken);

        Assert.Equal(200, response.StatusCode);
        Assert.DoesNotContain("secret-token", response.Body, StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(response.Body);
        Assert.Equal("partial", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(providerName, document.RootElement.GetProperty("provider").GetString());
        Assert.Equal("InvalidOperationException", document.RootElement.GetProperty("errorType").GetString());
        Assert.Equal("Diagnostics provider failed.", document.RootElement.GetProperty("message").GetString());
        Assert.False(document.RootElement.TryGetProperty(emptyShapeProperty, out _));
    }

    [Fact]
    public async Task Observability_registration_exposes_summary_endpoint_with_optional_subsystems_missing()
    {
        using var provider = new ServiceCollection()
            .AddLakonaGameObservability(new LakonaObservabilityOptions())
            .BuildServiceProvider();
        var router = provider.GetRequiredService<LakonaLocalAdminRouter>();

        var response = await router.RouteAsync(
            new LakonaLocalAdminRequest("GET", "/_lakona/diagnostics/summary", Stream.Null, true),
            TestContext.Current.CancellationToken);

        Assert.Equal(200, response.StatusCode);
        using var document = JsonDocument.Parse(response.Body);
        Assert.Equal("ok", document.RootElement.GetProperty("status").GetString());
        Assert.True(document.RootElement.GetProperty("sections").TryGetProperty("process", out _));
        Assert.True(document.RootElement.GetProperty("sections").TryGetProperty("actors", out _));
        Assert.True(document.RootElement.GetProperty("sections").TryGetProperty("sessions", out _));
        Assert.True(document.RootElement.GetProperty("sections").TryGetProperty("hotfix", out _));
    }

    [Fact]
    public async Task Hotfix_diagnostics_do_not_expose_raw_failure_message()
    {
        using var provider = new ServiceCollection()
            .AddSingleton<IHotfixManager>(new SensitiveFailureHotfixManager())
            .BuildServiceProvider();
        var sink = new BoundedDiagnosticsEventBuffer(8, LogLevel.Trace);
        var router = new LakonaLocalAdminRouter(DiagnosticsLocalAdminRoutes.Create(
            new LakonaDiagnosticsSnapshotService([new HotfixDiagnosticsProvider(provider)], sink),
            sink));

        var response = await router.RouteAsync(
            new LakonaLocalAdminRequest("GET", "/_lakona/diagnostics/summary", Stream.Null, true),
            TestContext.Current.CancellationToken);

        Assert.Equal(200, response.StatusCode);
        Assert.DoesNotContain("secret-hotfix-message", response.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("lastFailureMessage", response.Body, StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(response.Body);
        var hotfix = document.RootElement.GetProperty("sections").GetProperty("hotfix");
        Assert.Equal("Failed", hotfix.GetProperty("lastReloadStatus").GetString());
        Assert.Equal("InvalidOperationException", hotfix.GetProperty("lastFailureExceptionType").GetString());
    }

    [Fact]
    public async Task Events_endpoint_redacts_sensitive_message_fragments()
    {
        var sink = new BoundedDiagnosticsEventBuffer(8, LogLevel.Trace);
        sink.Publish(new DiagnosticsEvent(
            DateTimeOffset.UtcNow,
            LogLevel.Error,
            "Lakona.Game.Tests",
            "hotfix.reload.failed",
            "reload failed for secret-token at C:\\deploy\\private\\hotfix.dll",
            TraceId: null,
            CorrelationId: null,
            new Dictionary<string, string?> { ["provider"] = "hotfix token=abc123 /var/secrets/hotfix.dll" }));
        var router = new LakonaLocalAdminRouter(DiagnosticsLocalAdminRoutes.Create(
            new LakonaDiagnosticsSnapshotService([], sink),
            sink));

        var response = await router.RouteAsync(
            new LakonaLocalAdminRequest("GET", "/_lakona/diagnostics/events", Stream.Null, true),
            TestContext.Current.CancellationToken);

        Assert.Equal(200, response.StatusCode);
        Assert.DoesNotContain("secret-token", response.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("abc123", response.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\deploy\\private\\hotfix.dll", response.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/var/secrets/hotfix.dll", response.Body, StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(response.Body);
        var evt = Assert.Single(document.RootElement.GetProperty("events").EnumerateArray());
        Assert.Equal("Lakona.Game.Tests", evt.GetProperty("category").GetString());
        Assert.Equal("hotfix.reload.failed", evt.GetProperty("kind").GetString());
        Assert.Contains("[redacted", evt.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[redacted", evt.GetProperty("dimensions").GetProperty("provider").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Events_endpoint_redacts_sensitive_identity_fields()
    {
        var sink = new BoundedDiagnosticsEventBuffer(8, LogLevel.Trace);
        sink.Publish(new DiagnosticsEvent(
            DateTimeOffset.UtcNow,
            LogLevel.Error,
            "auth token=abc123",
            "reload C:\\deploy\\private\\kind",
            "recent event",
            TraceId: "Bearer abc.def.ghi",
            CorrelationId: "/var/secrets/correlation",
            new Dictionary<string, string?> { ["provider"] = "hotfix" }));
        var router = new LakonaLocalAdminRouter(DiagnosticsLocalAdminRoutes.Create(
            new LakonaDiagnosticsSnapshotService([], sink),
            sink));

        var response = await router.RouteAsync(
            new LakonaLocalAdminRequest("GET", "/_lakona/diagnostics/events", Stream.Null, true),
            TestContext.Current.CancellationToken);

        Assert.Equal(200, response.StatusCode);
        Assert.DoesNotContain("abc123", response.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\deploy\\private\\kind", response.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("abc.def.ghi", response.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/var/secrets/correlation", response.Body, StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(response.Body);
        var evt = Assert.Single(document.RootElement.GetProperty("events").EnumerateArray());
        Assert.Contains("[redacted", evt.GetProperty("category").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[redacted", evt.GetProperty("kind").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[redacted", evt.GetProperty("traceId").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[redacted", evt.GetProperty("correlationId").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("recent event", evt.GetProperty("message").GetString());
    }

    private sealed class TestSnapshotProvider : ILakonaDiagnosticsSnapshotProvider
    {
        private readonly object _snapshot;

        public TestSnapshotProvider(string name, object snapshot)
        {
            Name = name;
            _snapshot = snapshot;
        }

        public string Name { get; }

        public ValueTask<object> CaptureAsync(CancellationToken cancellationToken)
        {
            return new ValueTask<object>(_snapshot);
        }
    }

    private sealed class ThrowingSnapshotProvider : ILakonaDiagnosticsSnapshotProvider
    {
        public ThrowingSnapshotProvider(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public ValueTask<object> CaptureAsync(CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("failed with secret-token");
        }
    }

    private sealed class SensitiveFailureHotfixManager : IHotfixManager
    {
        public SensitiveFailureHotfixManager()
        {
            Current = new HotfixSnapshot(
                "loaded-v1",
                SourceKind: null,
                SourcePath: null,
                DateTimeOffset.UtcNow,
                DispatchTableVersion: 42,
                Methods: null,
                HotfixReloadStatus.Failed,
                "secret-hotfix-message from C:\\deploy\\private\\hotfix.dll",
                "InvalidOperationException");
        }

        public HotfixSnapshot Current { get; }

        public event EventHandler<HotfixReloadResult>? Reloaded
        {
            add { }
            remove { }
        }

        public ValueTask<HotfixReloadResult> ValidateAsync(CancellationToken cancellationToken = default)
        {
            return new ValueTask<HotfixReloadResult>(CreateResult());
        }

        public ValueTask<HotfixReloadResult> ValidateAsync(
            IHotfixAssemblySource source,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<HotfixReloadResult>(CreateResult());
        }

        public ValueTask<HotfixReloadResult> ReloadAsync(CancellationToken cancellationToken = default)
        {
            return new ValueTask<HotfixReloadResult>(CreateResult());
        }

        private HotfixReloadResult CreateResult()
        {
            return new HotfixReloadResult(
                HotfixReloadStatus.Failed,
                Current,
                RequestedVersion: null,
                RequestedPath: null,
                Diagnostics: null,
                ErrorMessage: "secret-hotfix-message",
                ExceptionType: "InvalidOperationException");
        }
    }
}
