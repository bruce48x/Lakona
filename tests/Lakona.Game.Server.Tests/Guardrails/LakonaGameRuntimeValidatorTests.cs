using Microsoft.Extensions.DependencyInjection;
using Lakona.Game.Server.Guardrails;
using Lakona.Game.Server.Guardrails.Rules;
using Xunit;

namespace Lakona.Game.Server.Tests.Guardrails;

public sealed class LakonaGameRuntimeValidatorTests
{
    [Fact]
    public void ValidationResult_Succeeds_WhenNoErrorDiagnosticsExist()
    {
        var result = new LakonaGameValidationResult(
            [
                new LakonaGameDiagnostic("LAKONA000", LakonaGameDiagnosticSeverity.Info, "ok"),
                new LakonaGameDiagnostic("LAKONA050", LakonaGameDiagnosticSeverity.Warning, "local default")
            ]);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void ValidationResult_Fails_WhenAnyErrorDiagnosticExists()
    {
        var result = new LakonaGameValidationResult(
            [
                new LakonaGameDiagnostic("LAKONA001", LakonaGameDiagnosticSeverity.Error, "Node id is required.")
            ]);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void ResolvedValue_PreservesValueSourceAndPath()
    {
        var value = new LakonaGameResolvedValue<string>(
            "dev-1",
            LakonaGameValueSource.Configuration,
            "Lakona:Node:Id");

        Assert.Equal("dev-1", value.Value);
        Assert.Equal(LakonaGameValueSource.Configuration, value.Source);
        Assert.Equal("Lakona:Node:Id", value.Path);
    }

    [Fact]
    public void ResolvedRuntime_CarriesCoreRuntimeSections()
    {
        var runtime = TestRuntime();

        Assert.Equal("dev-1", runtime.NodeId.Value);
        Assert.Equal("kcp", runtime.Endpoints[0].Transport.Value);
        Assert.Equal("Server.Hotfix.dll", runtime.Hotfix.AssemblyFileName.Value);
    }

    [Fact]
    public void RuntimeValidator_Fails_WhenNodeIdIsMissing()
    {
        var runtime = TestRuntime() with
        {
            NodeId = new LakonaGameResolvedValue<string>("", LakonaGameValueSource.Configuration, "Lakona:Node:Id")
        };
        var result = Validate(runtime);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "LAKONA001");
    }

    [Fact]
    public void RuntimeValidator_Fails_WhenWebSocketPathIsMissing()
    {
        var runtime = TestRuntime() with
        {
            Endpoints = [TestEndpoint("websocket", "127.0.0.1", 20000, path: "")]
        };
        var result = Validate(runtime);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "LAKONA023");
    }

    [Fact]
    public void RuntimeValidator_Fails_WhenHotfixAssemblyIsMissing()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "Server.Hotfix.dll");
        var runtime = TestRuntime() with
        {
            Hotfix = TestRuntime().Hotfix with
            {
                AssemblyPath = new LakonaGameResolvedValue<string>(missingPath, LakonaGameValueSource.GeneratedConvention)
            }
        };
        var result = Validate(runtime);

        Assert.False(result.Succeeded);
        var diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == "LAKONA071");
        Assert.Equal(LakonaGameDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("dotnet build Server/Hotfix/Server.Hotfix.csproj", diagnostic.Repair);
    }

    [Fact]
    public void EndpointRule_rejects_duplicate_transports()
    {
        var runtime = TestRuntime() with
        {
            Endpoints =
            [
                TestEndpoint("kcp", "127.0.0.1", 20000),
                TestEndpoint("kcp", "127.0.0.1", 20001)
            ]
        };

        var result = Validate(runtime);

        Assert.Contains(result.Diagnostics, d => d.Code == "LAKONA024");
    }

    [Fact]
    public void EndpointRule_rejects_missing_transport()
    {
        var runtime = TestRuntime() with
        {
            Endpoints = [TestEndpoint("", "127.0.0.1", 20000)]
        };

        var result = Validate(runtime);

        Assert.Contains(result.Diagnostics, d => d.Code == "LAKONA020");
    }

    [Fact]
    public void EndpointRule_rejects_missing_host()
    {
        var runtime = TestRuntime() with
        {
            Endpoints = [TestEndpoint("kcp", "", 20000)]
        };

        var result = Validate(runtime);

        Assert.Contains(result.Diagnostics, d => d.Code == "LAKONA021");
    }

    [Fact]
    public void EndpointRule_rejects_missing_serializer()
    {
        var runtime = TestRuntime() with
        {
            Endpoints = [TestEndpoint("kcp", "127.0.0.1", 20000, serializer: "")]
        };

        var result = Validate(runtime);

        Assert.Contains(result.Diagnostics, d => d.Code == "LAKONA028");
    }

    [Fact]
    public void EndpointRule_rejects_unknown_serializer()
    {
        var runtime = TestRuntime() with
        {
            Endpoints = [TestEndpoint("kcp", "127.0.0.1", 20000, serializer: "protobuf")]
        };

        var result = Validate(runtime);

        Assert.Contains(result.Diagnostics, d => d.Code == "LAKONA028");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void EndpointRule_rejects_invalid_port(int port)
    {
        var runtime = TestRuntime() with
        {
            Endpoints = [TestEndpoint("kcp", "127.0.0.1", port)]
        };

        var result = Validate(runtime);

        Assert.Contains(result.Diagnostics, d => d.Code == "LAKONA022");
    }

    [Fact]
    public void EndpointRule_rejects_unknown_transport()
    {
        var runtime = TestRuntime() with
        {
            Endpoints = [TestEndpoint("quic", "127.0.0.1", 20000)]
        };

        var result = Validate(runtime);

        Assert.Contains(result.Diagnostics, d => d.Code == "LAKONA020");
    }

    [Fact]
    public void EndpointRule_rejects_duplicate_bind_address()
    {
        var runtime = TestRuntime() with
        {
            Endpoints =
            [
                TestEndpoint("kcp", "127.0.0.1", 20000),
                TestEndpoint("tcp", "127.0.0.1", 20000)
            ]
        };

        var result = Validate(runtime);

        Assert.Contains(result.Diagnostics, d => d.Code == "LAKONA026");
    }

    [Fact]
    public void EndpointRule_rejects_websocket_without_path()
    {
        var runtime = TestRuntime() with
        {
            Endpoints = [TestEndpoint("websocket", "127.0.0.1", 20000, path: "")]
        };

        var result = Validate(runtime);

        Assert.Contains(result.Diagnostics, d => d.Code == "LAKONA023");
    }

    [Fact]
    public void EndpointRule_rejects_kcp_with_path()
    {
        var runtime = TestRuntime() with
        {
            Endpoints = [TestEndpoint("kcp", "127.0.0.1", 20000, path: "/bad")]
        };

        var result = Validate(runtime);

        Assert.Contains(result.Diagnostics, d => d.Code == "LAKONA025");
    }

    [Fact]
    public void EndpointRule_rejects_duplicate_rpc_services_within_endpoint()
    {
        var runtime = TestRuntime() with
        {
            Endpoints =
            [
                TestEndpoint(
                    "websocket",
                    "127.0.0.1",
                    20000,
                    path: "/ws",
                    rpcServices: ["login", "Login"])
            ]
        };

        var result = Validate(runtime);

        Assert.Contains(result.Diagnostics, d => d.Code == "LAKONA027");
    }

    [Theory]
    [InlineData(0, 1, 10)]
    [InlineData(10, 0, 10)]
    [InlineData(10, 11, 10)]
    [InlineData(10, 1, 0)]
    public void EndpointRule_rejects_invalid_connection_limits(
        int maxActiveConnections,
        int maxPendingHandshakes,
        int handshakeTimeoutSeconds)
    {
        var endpoint = TestEndpoint("kcp", "127.0.0.1", 20000) with
        {
            MaxActiveConnections = new LakonaGameResolvedValue<int>(maxActiveConnections, LakonaGameValueSource.Configuration),
            MaxPendingHandshakes = new LakonaGameResolvedValue<int>(maxPendingHandshakes, LakonaGameValueSource.Configuration),
            HandshakeTimeout = new LakonaGameResolvedValue<TimeSpan>(TimeSpan.FromSeconds(handshakeTimeoutSeconds), LakonaGameValueSource.Configuration)
        };
        var runtime = TestRuntime() with { Endpoints = [endpoint] };

        var result = Validate(runtime);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "LAKONA029");
    }

    [Fact]
    public void ClusterEndpointRule_rejects_missing_endpoint_when_cluster_is_configured()
    {
        var runtime = TestRuntime() with
        {
            ClusterEndpoint = new LakonaGameResolvedClusterEndpoint(
                Endpoint: new LakonaGameResolvedValue<string>("", LakonaGameValueSource.Configuration, "Lakona:Cluster:Endpoint"))
        };

        var result = Validate(runtime);

        Assert.Contains(result.Diagnostics, d => d.Code == "LAKONA040");
    }

    [Theory]
    [InlineData("tcp://127.0.0.1")]
    [InlineData("tcp://127.0.0.1:0")]
    [InlineData("tcp://:21000")]
    public void ClusterEndpointRule_rejects_unsupported_cluster_uri(string endpoint)
    {
        var runtime = TestRuntime() with
        {
            ClusterEndpoint = TestClusterEndpoint(endpoint)
        };

        var result = Validate(runtime);

        Assert.Contains(result.Diagnostics, d => d.Code == "LAKONA043");
    }

    [Fact]
    public void ClusterEndpointRule_rejects_business_port_conflict()
    {
        var runtime = TestRuntime() with
        {
            Endpoints = [TestEndpoint("kcp", "127.0.0.1", 20000)],
            ClusterEndpoint = TestClusterEndpoint("tcp://127.0.0.1:20000")
        };

        var result = Validate(runtime);

        Assert.Contains(result.Diagnostics, d => d.Code == "LAKONA042");
    }

    [Fact]
    public void RuntimeValidator_includes_management_admin_rule_by_default()
    {
        var services = new ServiceCollection();

        services.AddLakonaGameRuntimeValidation();

        using var provider = services.BuildServiceProvider();
        var validator = provider.GetRequiredService<LakonaGameRuntimeValidator>();
        var rules = provider.GetServices<ILakonaGameValidationRule>();

        Assert.NotNull(validator);
        Assert.Contains(rules, rule => rule is ManagementAdminRule);
    }

    [Fact]
    public void RuntimeValidator_Fails_WhenHeartbeatIntervalIsNotPositive()
    {
        var runtime = TestRuntime() with
        {
            Heartbeat = TestHeartbeat(interval: TimeSpan.Zero)
        };

        var result = Validate(runtime);

        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == "LAKONA090");
        Assert.Equal(LakonaGameDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Lakona:Heartbeat:Interval", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeValidator_Fails_WhenHeartbeatTimeoutIsNotPositive()
    {
        var runtime = TestRuntime() with
        {
            Heartbeat = TestHeartbeat(timeout: TimeSpan.Zero)
        };

        var result = Validate(runtime);

        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == "LAKONA091");
        Assert.Equal(LakonaGameDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Lakona:Heartbeat:Timeout", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeValidator_Fails_WhenHeartbeatTimeoutIsShorterThanInterval()
    {
        var runtime = TestRuntime() with
        {
            Heartbeat = TestHeartbeat(
                interval: TimeSpan.FromSeconds(30),
                timeout: TimeSpan.FromSeconds(10))
        };

        var result = Validate(runtime);

        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == "LAKONA092");
        Assert.Equal(LakonaGameDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("must not be shorter", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeValidator_Fails_WhenActorHostsContainBlankOrDuplicateNames()
    {
        var runtime = TestRuntime() with
        {
            ActorHosts =
            [
                new LakonaGameResolvedValue<string>("room", LakonaGameValueSource.Configuration, "Lakona:ActorHosts:0"),
                new LakonaGameResolvedValue<string>(" ", LakonaGameValueSource.Configuration, "Lakona:ActorHosts:1"),
                new LakonaGameResolvedValue<string>("Room", LakonaGameValueSource.Configuration, "Lakona:ActorHosts:2")
            ]
        };

        var result = Validate(runtime);

        Assert.Contains(result.Diagnostics, d => d.Code == "LAKONA101");
        Assert.Contains(result.Diagnostics, d => d.Code == "LAKONA102");
    }

    [Fact]
    public void RuntimeValidator_includes_heartbeat_rule_by_default()
    {
        var services = new ServiceCollection();

        services.AddLakonaGameRuntimeValidation();

        using var provider = services.BuildServiceProvider();
        var rules = provider.GetServices<ILakonaGameValidationRule>();

        Assert.Contains(rules, rule => rule is HeartbeatRule);
    }

    private static LakonaGameResolvedRuntime TestRuntime()
    {
        return new LakonaGameResolvedRuntime(
            NodeId: new LakonaGameResolvedValue<string>("dev-1", LakonaGameValueSource.Configuration, "Lakona:Node:Id"),
            Endpoints: [TestEndpoint("kcp", "127.0.0.1", 20000)],
            Cluster: new LakonaGameResolvedCluster(
                AdvertisedEndpoints: new Dictionary<string, string> { ["client"] = "kcp://127.0.0.1:20000" }),
            ClusterEndpoint: null,
            Hotfix: new LakonaGameResolvedHotfix(
                AssemblyPath: new LakonaGameResolvedValue<string>("Server.Hotfix.dll", LakonaGameValueSource.GeneratedConvention),
                AssemblyFileName: new LakonaGameResolvedValue<string>("Server.Hotfix.dll", LakonaGameValueSource.GeneratedConvention)),
            ReliablePush: new LakonaGameResolvedReliablePush(
                StorageMode: new LakonaGameResolvedValue<string>("InMemory", LakonaGameValueSource.Default),
                PendingLimit: new LakonaGameResolvedValue<int>(256, LakonaGameValueSource.Default),
                ResumeWindowSeconds: new LakonaGameResolvedValue<int>(60, LakonaGameValueSource.Default),
                HasSessionIdentityResolver: true),
            Heartbeat: TestHeartbeat(),
            Management: new LakonaGameResolvedManagement(
                AdminEnabled: new LakonaGameResolvedValue<bool>(false, LakonaGameValueSource.Default, "Lakona:Management:Admin:Enabled"),
                HttpHost: new LakonaGameResolvedValue<string>("127.0.0.1", LakonaGameValueSource.Default, "Lakona:Management:Http:Host"),
                AdminRequireLoopback: new LakonaGameResolvedValue<bool>(true, LakonaGameValueSource.Default, "Lakona:Management:Admin:RequireLoopback")));
    }

    private static LakonaGameResolvedEndpoint TestEndpoint(
        string transport,
        string host,
        int port,
        string serializer = "memorypack",
        string path = "",
        string advertisedHost = "",
        IReadOnlyList<string>? rpcServices = null)
    {
        return new LakonaGameResolvedEndpoint(
            Transport: new LakonaGameResolvedValue<string>(transport, LakonaGameValueSource.Configuration),
            Serializer: new LakonaGameResolvedValue<string>(serializer, LakonaGameValueSource.Configuration),
            Host: new LakonaGameResolvedValue<string>(host, LakonaGameValueSource.Configuration),
            Port: new LakonaGameResolvedValue<int>(port, LakonaGameValueSource.Configuration),
            Path: new LakonaGameResolvedValue<string>(path, LakonaGameValueSource.Configuration),
            AdvertisedHost: new LakonaGameResolvedValue<string>(advertisedHost, LakonaGameValueSource.Configuration),
            AdvertisedEndpoint: new LakonaGameResolvedValue<string>($"{transport}://{host}:{port}{path}", LakonaGameValueSource.GeneratedConvention),
            RpcServices: rpcServices ?? []);
    }

    private static LakonaGameResolvedClusterEndpoint TestClusterEndpoint(string endpoint)
    {
        return new LakonaGameResolvedClusterEndpoint(
            Endpoint: new LakonaGameResolvedValue<string>(endpoint, LakonaGameValueSource.Configuration, "Lakona:Cluster:Endpoint"));
    }

    private static LakonaGameValidationResult Validate(LakonaGameResolvedRuntime runtime)
    {
        var validator = new LakonaGameRuntimeValidator(
            [
                new NodeIdentityRule(),
                new EndpointRule(),
                new ClusterEndpointRule(),
                new HotfixSourceRule(),
                new HeartbeatRule(),
                new ActorHostConfigurationRule()
            ]);

        return validator.Validate(runtime);
    }

    private static LakonaGameResolvedHeartbeat TestHeartbeat(
        TimeSpan? interval = null,
        TimeSpan? timeout = null)
    {
        return new LakonaGameResolvedHeartbeat(
            Interval: new LakonaGameResolvedValue<TimeSpan>(
                interval ?? TimeSpan.FromSeconds(15),
                LakonaGameValueSource.Configuration,
                "Lakona:Heartbeat:Interval"),
            Timeout: new LakonaGameResolvedValue<TimeSpan>(
                timeout ?? TimeSpan.FromSeconds(45),
                LakonaGameValueSource.Configuration,
                "Lakona:Heartbeat:Timeout"));
    }
}
