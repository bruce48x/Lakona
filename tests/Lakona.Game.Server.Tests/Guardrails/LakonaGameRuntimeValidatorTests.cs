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
                new LakonaGameDiagnostic("ULINK000", LakonaGameDiagnosticSeverity.Info, "ok"),
                new LakonaGameDiagnostic("ULINK050", LakonaGameDiagnosticSeverity.Warning, "local default")
            ]);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void ValidationResult_Fails_WhenAnyErrorDiagnosticExists()
    {
        var result = new LakonaGameValidationResult(
            [
                new LakonaGameDiagnostic("ULINK001", LakonaGameDiagnosticSeverity.Error, "Node id is required.")
            ]);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void ResolvedValue_PreservesValueSourceAndPath()
    {
        var value = new LakonaGameResolvedValue<string>(
            "dev-1",
            LakonaGameValueSource.Configuration,
            "Lakona.Game:Node:Id");

        Assert.Equal("dev-1", value.Value);
        Assert.Equal(LakonaGameValueSource.Configuration, value.Source);
        Assert.Equal("Lakona.Game:Node:Id", value.Path);
    }

    [Fact]
    public void ResolvedRuntime_CarriesCoreRuntimeSections()
    {
        var runtime = TestRuntime();

        Assert.Equal("dev-1", runtime.NodeId.Value);
        Assert.Equal("kcp", runtime.Endpoints[0].Transport.Value);
        Assert.Equal("Server.Hotfix.dll", runtime.Hotfix.AssemblyFileName.Value);
        Assert.Equal(LakonaGameRuntimeProfile.Development, runtime.Profile);
    }

    [Fact]
    public void RuntimeValidator_Fails_WhenNodeIdIsMissing()
    {
        var runtime = TestRuntime() with
        {
            NodeId = new LakonaGameResolvedValue<string>("", LakonaGameValueSource.Configuration, "Lakona.Game:Node:Id")
        };
        var result = Validate(runtime);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "ULINK001");
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
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "ULINK023");
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
        var diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == "ULINK071");
        Assert.Equal(LakonaGameDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("dotnet build Server/Hotfix/Server.Hotfix.csproj", diagnostic.Repair);
    }

    [Fact]
    public void RuntimeValidator_Fails_WhenClusterServiceNameIsDuplicated()
    {
        var runtime = TestRuntime() with
        {
            Cluster = TestRuntime().Cluster with
            {
                Services =
                [
                    new LakonaGameResolvedClusterService("gateway", "gateway"),
                    new LakonaGameResolvedClusterService("gateway", "gateway")
                ]
            }
        };
        var result = Validate(runtime);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "ULINK041");
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

        Assert.Contains(result.Diagnostics, d => d.Code == "ULINK024");
    }

    [Fact]
    public void EndpointRule_rejects_missing_transport()
    {
        var runtime = TestRuntime() with
        {
            Endpoints = [TestEndpoint("", "127.0.0.1", 20000)]
        };

        var result = Validate(runtime);

        Assert.Contains(result.Diagnostics, d => d.Code == "ULINK020");
    }

    [Fact]
    public void EndpointRule_rejects_missing_host()
    {
        var runtime = TestRuntime() with
        {
            Endpoints = [TestEndpoint("kcp", "", 20000)]
        };

        var result = Validate(runtime);

        Assert.Contains(result.Diagnostics, d => d.Code == "ULINK021");
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

        Assert.Contains(result.Diagnostics, d => d.Code == "ULINK022");
    }

    [Fact]
    public void EndpointRule_rejects_unknown_transport()
    {
        var runtime = TestRuntime() with
        {
            Endpoints = [TestEndpoint("quic", "127.0.0.1", 20000)]
        };

        var result = Validate(runtime);

        Assert.Contains(result.Diagnostics, d => d.Code == "ULINK020");
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

        Assert.Contains(result.Diagnostics, d => d.Code == "ULINK026");
    }

    [Fact]
    public void EndpointRule_rejects_websocket_without_path()
    {
        var runtime = TestRuntime() with
        {
            Endpoints = [TestEndpoint("websocket", "127.0.0.1", 20000, path: "")]
        };

        var result = Validate(runtime);

        Assert.Contains(result.Diagnostics, d => d.Code == "ULINK023");
    }

    [Fact]
    public void EndpointRule_rejects_kcp_with_path()
    {
        var runtime = TestRuntime() with
        {
            Endpoints = [TestEndpoint("kcp", "127.0.0.1", 20000, path: "/bad")]
        };

        var result = Validate(runtime);

        Assert.Contains(result.Diagnostics, d => d.Code == "ULINK025");
    }

    [Fact]
    public void ClusterEndpointRule_rejects_missing_endpoint_when_cluster_is_configured()
    {
        var runtime = TestRuntime() with
        {
            ClusterEndpoint = new LakonaGameResolvedClusterEndpoint(
                Endpoint: new LakonaGameResolvedValue<string>("", LakonaGameValueSource.Configuration, "Lakona.Game:Cluster:Endpoint"),
                Seeds: [])
        };

        var result = Validate(runtime);

        Assert.Contains(result.Diagnostics, d => d.Code == "ULINK040");
    }

    [Theory]
    [InlineData("udp://127.0.0.1:21000")]
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

        Assert.Contains(result.Diagnostics, d => d.Code == "ULINK043");
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

        Assert.Contains(result.Diagnostics, d => d.Code == "ULINK042");
    }

    [Fact]
    public void AddLakonaGameRuntimeValidation_RegistersDefaultValidator()
    {
        var services = new ServiceCollection();

        services.AddLakonaGameRuntimeValidation();

        using var provider = services.BuildServiceProvider();
        var validator = provider.GetRequiredService<LakonaGameRuntimeValidator>();

        Assert.NotNull(validator);
    }

    private static LakonaGameResolvedRuntime TestRuntime()
    {
        return new LakonaGameResolvedRuntime(
            NodeId: new LakonaGameResolvedValue<string>("dev-1", LakonaGameValueSource.Configuration, "Lakona.Game:Node:Id"),
            Endpoints: [TestEndpoint("kcp", "127.0.0.1", 20000)],
            Cluster: new LakonaGameResolvedCluster(
                Services: [new LakonaGameResolvedClusterService("gateway", "gateway")],
                AdvertisedEndpoints: new Dictionary<string, string> { ["client"] = "kcp://127.0.0.1:20000" }),
            ClusterEndpoint: null,
            Feature: new LakonaGameResolvedFeature(
                Configured: null,
                Active: [],
                StartupOrder: []),
            Hotfix: new LakonaGameResolvedHotfix(
                AssemblyPath: new LakonaGameResolvedValue<string>("Server.Hotfix.dll", LakonaGameValueSource.GeneratedConvention),
                AssemblyFileName: new LakonaGameResolvedValue<string>("Server.Hotfix.dll", LakonaGameValueSource.GeneratedConvention)),
            ReliablePush: new LakonaGameResolvedReliablePush(
                StorageMode: new LakonaGameResolvedValue<string>("InMemory", LakonaGameValueSource.Default),
                PendingLimit: new LakonaGameResolvedValue<int>(256, LakonaGameValueSource.Default),
                ReplayWindowSeconds: new LakonaGameResolvedValue<int>(120, LakonaGameValueSource.Default),
                HasSessionIdentityResolver: true),
            Profile: LakonaGameRuntimeProfile.Development);
    }

    private static LakonaGameResolvedEndpoint TestEndpoint(
        string transport,
        string host,
        int port,
        string path = "",
        string advertisedHost = "")
    {
        return new LakonaGameResolvedEndpoint(
            Transport: new LakonaGameResolvedValue<string>(transport, LakonaGameValueSource.Configuration),
            Host: new LakonaGameResolvedValue<string>(host, LakonaGameValueSource.Configuration),
            Port: new LakonaGameResolvedValue<int>(port, LakonaGameValueSource.Configuration),
            Path: new LakonaGameResolvedValue<string>(path, LakonaGameValueSource.Configuration),
            AdvertisedHost: new LakonaGameResolvedValue<string>(advertisedHost, LakonaGameValueSource.Configuration),
            AdvertisedEndpoint: new LakonaGameResolvedValue<string>($"{transport}://{host}:{port}{path}", LakonaGameValueSource.GeneratedConvention));
    }

    private static LakonaGameResolvedClusterEndpoint TestClusterEndpoint(string endpoint)
    {
        return new LakonaGameResolvedClusterEndpoint(
            Endpoint: new LakonaGameResolvedValue<string>(endpoint, LakonaGameValueSource.Configuration, "Lakona.Game:Cluster:Endpoint"),
            Seeds: []);
    }

    private static LakonaGameValidationResult Validate(LakonaGameResolvedRuntime runtime)
    {
        var validator = new LakonaGameRuntimeValidator(
            [
                new NodeIdentityRule(),
                new EndpointRule(),
                new ClusterEndpointRule(),
                new HotfixSourceRule(),
                new ClusterServiceGraphRule()
            ]);

        return validator.Validate(runtime);
    }
}
