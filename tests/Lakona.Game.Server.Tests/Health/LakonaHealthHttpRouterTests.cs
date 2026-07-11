using System.Text.Json;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Guardrails;
using Lakona.Game.Server.Health;
using Lakona.Game.Server.Observability;
using Xunit;

namespace Lakona.Game.Server.Tests.Health;

public sealed class LakonaHealthHttpRouterTests
{
    [Fact]
    public async Task Live_endpoint_returns_ok_json()
    {
        var router = new LakonaHealthHttpRouter([LakonaHealthHttpRoutes.Live()]);

        var response = await router.RouteAsync(
            new LakonaHealthHttpRequest(
                "GET",
                "/_lakona/health/live",
                RemoteAddressIsLoopback: true,
                RequireLoopback: true),
            TestContext.Current.CancellationToken);

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("application/json", response.ContentType);
        using var document = JsonDocument.Parse(response.Body);
        Assert.Equal("ok", document.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Non_loopback_health_request_returns_403_without_dispatching_route()
    {
        var route = new RecordingHealthRoute();
        var router = new LakonaHealthHttpRouter([route]);

        var response = await router.RouteAsync(
            new LakonaHealthHttpRequest(
                "GET",
                "/_lakona/health/live",
                RemoteAddressIsLoopback: false,
                RequireLoopback: true),
            TestContext.Current.CancellationToken);

        Assert.Equal(403, response.StatusCode);
        Assert.Empty(route.Requests);
    }

    [Fact]
    public async Task Ready_endpoint_returns_503_with_guardrail_diagnostics_when_runtime_is_not_ready()
    {
        var runtime = new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "dev-1" },
            Endpoints =
            [
                new LakonaGameEndpointOptions
                {
                    Transport = "websocket",
                    Serializer = "json",
                    Host = "127.0.0.1",
                    Port = 20000,
                    Path = "/ws"
                }
            ]
        };
        var evaluator = new LakonaGameReadinessEvaluator(
            runtime,
            runtime.ToClusterOptions(),
            new LakonaObservabilityCapabilities(),
            new LakonaHealthReadinessState(Path.Combine(Path.GetTempPath(), "missing-hotfix.dll")),
            new LakonaGameRuntimeValidator([new AlwaysFailsRule()]));
        var router = new LakonaHealthHttpRouter([LakonaHealthHttpRoutes.Ready(evaluator)]);

        var response = await router.RouteAsync(
            new LakonaHealthHttpRequest(
                "GET",
                "/_lakona/health/ready",
                RemoteAddressIsLoopback: true,
                RequireLoopback: true),
            TestContext.Current.CancellationToken);

        Assert.Equal(503, response.StatusCode);
        using var document = JsonDocument.Parse(response.Body);
        Assert.Equal("not_ready", document.RootElement.GetProperty("status").GetString());
        Assert.False(document.RootElement.GetProperty("succeeded").GetBoolean());
        var diagnostic = Assert.Single(document.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("LAKONA999", diagnostic.GetProperty("code").GetString());
    }

    private sealed class RecordingHealthRoute : ILakonaHealthHttpRoute
    {
        public string Method => "GET";

        public string Path => "/_lakona/health/live";

        public List<LakonaHealthHttpRequest> Requests { get; } = [];

        public ValueTask<LakonaHealthHttpResponse> HandleAsync(
            LakonaHealthHttpRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return new ValueTask<LakonaHealthHttpResponse>(
                LakonaHealthHttpResponse.Json(new { status = "ok" }));
        }
    }

    private sealed class AlwaysFailsRule : ILakonaGameValidationRule
    {
        public IEnumerable<LakonaGameDiagnostic> Validate(LakonaGameResolvedRuntime runtime)
        {
            yield return new LakonaGameDiagnostic(
                "LAKONA999",
                LakonaGameDiagnosticSeverity.Error,
                "Runtime is not ready.",
                "Fix runtime configuration.");
        }
    }
}
