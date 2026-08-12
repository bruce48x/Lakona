using System.Text.Json;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Guardrails;
using Lakona.Game.Server.Health;
using Lakona.Game.Server.Observability;
using Lakona.Game.Cluster;
using Microsoft.Extensions.DependencyInjection;
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

    [Fact]
    public async Task Cluster_endpoint_returns_local_committed_snapshot_without_node_names()
    {
        var cluster = new ClusterIncarnationId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var membership = new StaticMembership(new ClusterMembershipSnapshot(cluster, new MembershipViewId(9),
        [
            Member("data-1", cluster, 21001), Member("gateway-1", cluster, 21002), Member("battle-1", cluster, 21003)
        ]));
        var runtime = new LakonaGameRuntimeOptions
        {
            Health = new LakonaHealthOptions { ClusterDiagnosticsEnabled = true }
        };
        var router = new LakonaHealthHttpRouter([new LakonaHealthHttpRoutes.ClusterRoute(membership, runtime)]);
        var response = await router.RouteAsync(
            new LakonaHealthHttpRequest("GET", "/_lakona/health/cluster", true, true),
            TestContext.Current.CancellationToken);

        Assert.Equal(200, response.StatusCode);
        using var document = JsonDocument.Parse(response.Body);
        Assert.Equal(cluster.Value.ToString(), document.RootElement.GetProperty("cluster").GetString());
        Assert.Equal(9, document.RootElement.GetProperty("view").GetInt64());
        Assert.Equal(3, document.RootElement.GetProperty("members").GetArrayLength());
        Assert.All(document.RootElement.GetProperty("members").EnumerateArray(), member => Assert.Equal("ready", member.GetProperty("state").GetString()));
        Assert.DoesNotContain("node", response.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Health_registration_exposes_cluster_route_only_when_explicitly_enabled()
    {
        var cluster = new ClusterIncarnationId(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var membership = new StaticMembership(new ClusterMembershipSnapshot(cluster, new MembershipViewId(1), [Member("data-1", cluster, 21001)]));
        await using var disabled = CreateHealthProvider(LakonaHealthOptions.Defaults(), membership);
        await using var enabled = CreateHealthProvider(new LakonaHealthOptions { ClusterDiagnosticsEnabled = true }, membership);

        var request = new LakonaHealthHttpRequest("GET", "/_lakona/health/cluster", true, true);
        var disabledResponse = await new LakonaHealthHttpRouter(disabled.GetServices<ILakonaHealthHttpRoute>())
            .RouteAsync(request, TestContext.Current.CancellationToken);
        var enabledResponse = await new LakonaHealthHttpRouter(enabled.GetServices<ILakonaHealthHttpRoute>())
            .RouteAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(404, disabledResponse.StatusCode);
        Assert.Equal(200, enabledResponse.StatusCode);
    }

    [Fact]
    public async Task Cluster_route_uses_authoritative_runtime_options_at_request_time()
    {
        var cluster = new ClusterIncarnationId(Guid.Parse("33333333-3333-3333-3333-333333333333"));
        var membership = new StaticMembership(new ClusterMembershipSnapshot(
            cluster,
            new MembershipViewId(1),
            [Member("gateway-1", cluster, 21002)]));
        var runtime = new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "gateway-1" },
            Health = new LakonaHealthOptions { ClusterDiagnosticsEnabled = true }
        };
        var services = new ServiceCollection();
        services.AddSingleton(runtime);
        services.AddSingleton<IClusterMembership>(membership);
        services.AddSingleton(new LakonaGameReadinessEvaluator(runtime, runtime.ToClusterOptions(), new LakonaHealthReadinessState("test.dll"), new LakonaGameRuntimeValidator([])));
        services.AddLakonaGameHealth();
        await using var provider = services.BuildServiceProvider();

        var response = await new LakonaHealthHttpRouter(provider.GetServices<ILakonaHealthHttpRoute>())
            .RouteAsync(new LakonaHealthHttpRequest("GET", "/_lakona/health/cluster", true, true), TestContext.Current.CancellationToken);

        Assert.Equal(200, response.StatusCode);
    }

    private static ServiceProvider CreateHealthProvider(LakonaHealthOptions options, IClusterMembership membership)
    {
        var runtime = new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "test" },
            Health = options
        };
        var services = new ServiceCollection();
        services.AddSingleton(runtime);
        services.AddSingleton<IClusterMembership>(membership);
        services.AddSingleton(new LakonaGameReadinessEvaluator(runtime, runtime.ToClusterOptions(), new LakonaHealthReadinessState("test.dll"), new LakonaGameRuntimeValidator([])));
        services.AddLakonaGameHealth();
        return services.BuildServiceProvider();
    }

    private static ClusterMember Member(string node, ClusterIncarnationId cluster, int port)
    {
        return new ClusterMember(new NodeReference(cluster, new NodeId(node), NodeIncarnationId.New()), ClusterMemberState.Ready, new NodeEndpoint($"tcp://127.0.0.1:{port}"), isVoter: true);
    }

    private sealed class StaticMembership(ClusterMembershipSnapshot current) : IClusterMembership
    {
        public ClusterMembershipSnapshot Current { get; } = current;
        public ValueTask<ClusterMembershipSnapshot> WaitForChangeAsync(MembershipViewId after, CancellationToken cancellationToken = default) => new(Current);
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
