using Microsoft.Extensions.DependencyInjection;
using Lakona.Game.Server.Guardrails;
using Lakona.Game.Server.Configuration;
using Xunit;

namespace Lakona.Game.Server.Tests.Guardrails;

public sealed class LakonaGameRuntimeValidatorTests
{
    [Fact]
    public void ValidationResult_Succeeds_WhenNoErrorDiagnosticsExist()
    {
        var result = new LakonaGameValidationResult(
            [
                new LakonaGameDiagnostic("LAKONA10000", LakonaGameDiagnosticSeverity.Info, "ok"),
                new LakonaGameDiagnostic("LAKONA10050", LakonaGameDiagnosticSeverity.Warning, "local default")
            ]);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void ValidationResult_Fails_WhenAnyErrorDiagnosticExists()
    {
        var result = new LakonaGameValidationResult(
            [
                new LakonaGameDiagnostic("LAKONA10001", LakonaGameDiagnosticSeverity.Error, "Node id is required.")
            ]);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void RuntimeValidator_Fails_WhenNodeIdIsMissing()
    {
        var runtime = new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "" }
        };
        var result = Validate(runtime);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "LAKONA10001");
    }

    [Fact]
    public void RuntimeValidator_Fails_WhenWebSocketPathIsMissing()
    {
        var runtime = new LakonaGameRuntimeOptions
        {
            Endpoints = [TestEndpoint("websocket", "127.0.0.1", 20000, path: "")]
        };
        var result = Validate(runtime);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "LAKONA10023");
    }

    [Fact]
    public void RuntimeValidator_Fails_WhenHotfixAssemblyIsMissing()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "Server.Hotfix.dll");
        var result = Validate(TestRuntime(), missingPath);

        Assert.False(result.Succeeded);
        var diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == "LAKONA10071");
        Assert.Equal(LakonaGameDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("dotnet build Server/Hotfix/Server.Hotfix.csproj", diagnostic.Repair);
    }

    [Fact]
    public void EndpointRule_rejects_duplicate_transports()
    {
        var runtime = new LakonaGameRuntimeOptions
        {
            Endpoints =
            [
                TestEndpoint("kcp", "127.0.0.1", 20000),
                TestEndpoint("kcp", "127.0.0.1", 20001)
            ]
        };

        var result = Validate(runtime);

        Assert.Contains(result.Diagnostics, d => d.Code == "LAKONA10024");
    }

    [Fact]
    public void EndpointRule_rejects_missing_transport()
    {
        var runtime = new LakonaGameRuntimeOptions
        {
            Endpoints = [TestEndpoint("", "127.0.0.1", 20000)]
        };

        var result = Validate(runtime);

        Assert.Contains(result.Diagnostics, d => d.Code == "LAKONA10020");
    }

    [Fact]
    public void EndpointRule_rejects_missing_host()
    {
        var runtime = new LakonaGameRuntimeOptions
        {
            Endpoints = [TestEndpoint("kcp", "", 20000)]
        };

        var result = Validate(runtime);

        Assert.Contains(result.Diagnostics, d => d.Code == "LAKONA10021");
    }

    [Fact]
    public void EndpointRule_rejects_missing_serializer()
    {
        var runtime = new LakonaGameRuntimeOptions
        {
            Endpoints = [TestEndpoint("kcp", "127.0.0.1", 20000, serializer: "")]
        };

        var result = Validate(runtime);

        Assert.Contains(result.Diagnostics, d => d.Code == "LAKONA10028");
    }

    [Fact]
    public void EndpointRule_rejects_unknown_serializer()
    {
        var runtime = new LakonaGameRuntimeOptions
        {
            Endpoints = [TestEndpoint("kcp", "127.0.0.1", 20000, serializer: "protobuf")]
        };

        var result = Validate(runtime);

        Assert.Contains(result.Diagnostics, d => d.Code == "LAKONA10028");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void EndpointRule_rejects_invalid_port(int port)
    {
        var runtime = new LakonaGameRuntimeOptions
        {
            Endpoints = [TestEndpoint("kcp", "127.0.0.1", port)]
        };

        var result = Validate(runtime);

        Assert.Contains(result.Diagnostics, d => d.Code == "LAKONA10022");
    }

    [Fact]
    public void EndpointRule_rejects_unknown_transport()
    {
        var runtime = new LakonaGameRuntimeOptions
        {
            Endpoints = [TestEndpoint("quic", "127.0.0.1", 20000)]
        };

        var result = Validate(runtime);

        Assert.Contains(result.Diagnostics, d => d.Code == "LAKONA10020");
    }

    [Fact]
    public void EndpointRule_rejects_duplicate_bind_address()
    {
        var runtime = new LakonaGameRuntimeOptions
        {
            Endpoints =
            [
                TestEndpoint("kcp", "127.0.0.1", 20000),
                TestEndpoint("tcp", "127.0.0.1", 20000)
            ]
        };

        var result = Validate(runtime);

        Assert.Contains(result.Diagnostics, d => d.Code == "LAKONA10026");
    }

    [Fact]
    public void EndpointRule_rejects_websocket_without_path()
    {
        var runtime = new LakonaGameRuntimeOptions
        {
            Endpoints = [TestEndpoint("websocket", "127.0.0.1", 20000, path: "")]
        };

        var result = Validate(runtime);

        Assert.Contains(result.Diagnostics, d => d.Code == "LAKONA10023");
    }

    [Fact]
    public void EndpointRule_rejects_kcp_with_path()
    {
        var runtime = new LakonaGameRuntimeOptions
        {
            Endpoints = [TestEndpoint("kcp", "127.0.0.1", 20000, path: "/bad")]
        };

        var result = Validate(runtime);

        Assert.Contains(result.Diagnostics, d => d.Code == "LAKONA10025");
    }

    [Fact]
    public void EndpointRule_rejects_duplicate_rpc_services_within_endpoint()
    {
        var runtime = new LakonaGameRuntimeOptions
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

        Assert.Contains(result.Diagnostics, d => d.Code == "LAKONA10027");
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
        var endpoint = new LakonaGameEndpointOptions
        {
            Transport = "kcp", Serializer = "memorypack", Host = "127.0.0.1", Port = 20000,
            ConnectionLimits = new LakonaGameEndpointConnectionLimitsOptions
            {
                MaxActiveConnections = maxActiveConnections,
                MaxPendingHandshakes = maxPendingHandshakes,
                HandshakeTimeout = TimeSpan.FromSeconds(handshakeTimeoutSeconds)
            }
        };
        var runtime = new LakonaGameRuntimeOptions { Endpoints = [endpoint] };

        var result = Validate(runtime);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "LAKONA10029");
    }

    [Fact]
    public void ClusterEndpointRule_rejects_missing_endpoint_when_cluster_is_configured()
    {
        var runtime = new LakonaGameRuntimeOptions
        {
            Cluster = new LakonaGameClusterOptions { Endpoint = "" }
        };

        var result = Validate(runtime);

        Assert.Contains(result.Diagnostics, d => d.Code == "LAKONA10040");
    }

    [Theory]
    [InlineData("tcp://127.0.0.1")]
    [InlineData("tcp://127.0.0.1:0")]
    [InlineData("tcp://:21000")]
    public void ClusterEndpointRule_rejects_unsupported_cluster_uri(string endpoint)
    {
        var runtime = new LakonaGameRuntimeOptions
        {
            Cluster = new LakonaGameClusterOptions { Endpoint = endpoint }
        };

        var result = Validate(runtime);

        Assert.Contains(result.Diagnostics, d => d.Code == "LAKONA10043");
    }

    [Fact]
    public void ClusterEndpointRule_rejects_business_port_conflict()
    {
        var runtime = new LakonaGameRuntimeOptions
        {
            Endpoints = [TestEndpoint("kcp", "127.0.0.1", 20000)],
            Cluster = new LakonaGameClusterOptions { Endpoint = "tcp://127.0.0.1:20000" }
        };

        var result = Validate(runtime);

        Assert.Contains(result.Diagnostics, d => d.Code == "LAKONA10042");
    }

    [Fact]
    public void RuntimeValidator_Fails_WhenHeartbeatIntervalIsNotPositive()
    {
        var runtime = new LakonaGameRuntimeOptions
        {
            Heartbeat = TestHeartbeat(interval: TimeSpan.Zero)
        };

        var result = Validate(runtime);

        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == "LAKONA10090");
        Assert.Equal(LakonaGameDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Lakona:Heartbeat:Interval", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeValidator_Fails_WhenHeartbeatTimeoutIsNotPositive()
    {
        var runtime = new LakonaGameRuntimeOptions
        {
            Heartbeat = TestHeartbeat(timeout: TimeSpan.Zero)
        };

        var result = Validate(runtime);

        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == "LAKONA10091");
        Assert.Equal(LakonaGameDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Lakona:Heartbeat:Timeout", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeValidator_Fails_WhenHeartbeatTimeoutIsShorterThanInterval()
    {
        var runtime = new LakonaGameRuntimeOptions
        {
            Heartbeat = TestHeartbeat(
                interval: TimeSpan.FromSeconds(30),
                timeout: TimeSpan.FromSeconds(10))
        };

        var result = Validate(runtime);

        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == "LAKONA10092");
        Assert.Equal(LakonaGameDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("must not be shorter", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeValidator_Fails_WhenNodeRolesContainBlankOrDuplicateNames()
    {
        var runtime = new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "dev-1", Roles = ["data", " ", "Data"] }
        };

        var result = Validate(runtime);

        Assert.Contains(result.Diagnostics, d => d.Code == "LAKONA10101");
        Assert.Contains(result.Diagnostics, d => d.Code == "LAKONA10102");
    }

    [Fact]
    public void Registered_validator_checks_real_configuration_and_preserves_indexed_paths()
    {
        var services = new ServiceCollection();
        services.AddLakonaGameRuntimeValidation();
        services.AddLakonaGameRuntimeValidation();
        using var provider = services.BuildServiceProvider();
        var validator = Assert.Single(provider.GetServices<LakonaGameRuntimeValidator>());
        var runtime = new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "dev-1", Roles = ["data", "Data"] },
            Endpoints = [TestEndpoint("tcp", "127.0.0.1", 20000), TestEndpoint("websocket", "127.0.0.1", 20001)],
            Heartbeat = TestHeartbeat(interval: TimeSpan.Zero),
            Management = new LakonaManagementOptions
            {
                Http = new LakonaManagementHttpOptions { Host = "0.0.0.0" },
                Admin = new LakonaManagementAdminOptions { Enabled = true, RequireLoopback = true }
            }
        };
        var result = validator.Validate(runtime, hotfixAssemblyPath: typeof(LakonaGameRuntimeValidatorTests).Assembly.Location);
        Assert.Contains(result.Diagnostics, d => d.Code == "LAKONA10023" && d.Message.StartsWith("Lakona:Endpoints:1:Path:"));
        Assert.Contains(result.Diagnostics, d => d.Code == "LAKONA10102" && d.Message.StartsWith("Lakona:Node:Roles:1:"));
        Assert.Contains(result.Diagnostics, d => d.Code == "LAKONA10090");
        Assert.Contains(result.Diagnostics, d => d.Code == "LAKONA10130");
    }

    [Fact]
    public void Validator_uses_effective_cluster_identity_and_accepts_valid_runtime()
    {
        var runtime = TestRuntime();
        var validator = new LakonaGameRuntimeValidator();
        var path = typeof(LakonaGameRuntimeValidatorTests).Assembly.Location;
        Assert.True(validator.Validate(runtime, hotfixAssemblyPath: path).Succeeded);
        var cluster = new ClusterOptions { NodeId = "" };
        Assert.Contains(validator.Validate(runtime, cluster, path).Diagnostics, d => d.Code == "LAKONA10001");
    }

    private static LakonaGameRuntimeOptions TestRuntime() => new()
    {
        Node = new LakonaGameNodeOptions { Id = "dev-1" },
        Endpoints = [TestEndpoint("kcp", "127.0.0.1", 20000)]
    };

    private static LakonaGameEndpointOptions TestEndpoint(
        string transport, string host, int port, string serializer = "memorypack",
        string path = "", string advertisedHost = "", IReadOnlyList<string>? rpcServices = null) => new()
    {
        Transport = transport, Serializer = serializer, Host = host, Port = port,
        Path = path, AdvertisedHost = advertisedHost, RpcServices = rpcServices ?? []
    };

    private static LakonaGameValidationResult Validate(LakonaGameRuntimeOptions runtime, string? hotfixPath = null)
        => new LakonaGameRuntimeValidator().Validate(runtime,
            hotfixAssemblyPath: hotfixPath ?? typeof(LakonaGameRuntimeValidatorTests).Assembly.Location);

    private static LakonaGameHeartbeatOptions TestHeartbeat(TimeSpan? interval = null, TimeSpan? timeout = null) => new()
    {
        Interval = interval ?? TimeSpan.FromSeconds(15),
        Timeout = timeout ?? TimeSpan.FromSeconds(45)
    };
}
