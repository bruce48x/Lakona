using System.Text.Json;
using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Cluster.Sql;
using Lakona.Game.Server.Features;
using Lakona.Game.Server.Hosting;
using Lakona.Game.Server.Sessions;
using Lakona.Rpc.Serializer.Json;
using Lakona.Rpc.Server;
using Lakona.Rpc.Transport.Tcp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Server.App.Features;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace Agar.Unity.Tests;

public sealed class DistributedTopologyConfigurationTests
{
    [Fact]
    public void DataNodeOwnsStateAndClusterEndpointWithoutClientEndpoints()
    {
        using var document = Open("appsettings.data-1.json");
        var lakona = document.RootElement.GetProperty("Lakona");

        Assert.Equal("data-1", lakona.GetProperty("Node").GetProperty("Id").GetString());
        AssertFeatureSet(lakona, "database", "state-store", "matchmaking", "leaderboard");
        Assert.False(lakona.TryGetProperty("Endpoints", out _));
        Assert.Equal("tcp://10.0.0.1:21001", lakona.GetProperty("Cluster").GetProperty("Endpoint").GetString());
        Assert.True(document.RootElement.GetProperty("ConnectionStrings").TryGetProperty("AgarPostgres", out _));
        Assert.True(document.RootElement.GetProperty("ConnectionStrings").TryGetProperty("AgarRedis", out _));
    }

    [Fact]
    public void GatewayNodeOwnsOnlyWebSocketClientEndpoint()
    {
        using var document = Open("appsettings.gateway-1.json");
        var lakona = document.RootElement.GetProperty("Lakona");

        Assert.Equal("gateway-1", lakona.GetProperty("Node").GetProperty("Id").GetString());
        Assert.Empty(lakona.GetProperty("Feature").EnumerateArray());

        var endpoint = Assert.Single(lakona.GetProperty("Endpoints").EnumerateArray());
        Assert.Equal("websocket", endpoint.GetProperty("Transport").GetString());
        Assert.Equal("memorypack", endpoint.GetProperty("Serializer").GetString());
        Assert.Equal("/ws", endpoint.GetProperty("Path").GetString());
        Assert.Equal(new[] { "login", "player" }, endpoint.GetProperty("RpcServices").EnumerateArray().Select(item => item.GetString()).ToArray());
    }

    [Fact]
    public void BattleNodeOwnsRuntimeAndKcpEndpoint()
    {
        using var document = Open("appsettings.battle-1.json");
        var lakona = document.RootElement.GetProperty("Lakona");

        Assert.Equal("battle-1", lakona.GetProperty("Node").GetProperty("Id").GetString());
        AssertFeatureSet(lakona, "battle-runtime");

        var endpoint = Assert.Single(lakona.GetProperty("Endpoints").EnumerateArray());
        Assert.Equal("kcp", endpoint.GetProperty("Transport").GetString());
        Assert.Equal("memorypack", endpoint.GetProperty("Serializer").GetString());
        Assert.False(endpoint.TryGetProperty("Path", out _));
        Assert.Equal(new[] { "battle" }, endpoint.GetProperty("RpcServices").EnumerateArray().Select(item => item.GetString()).ToArray());
    }

    [Fact]
    public void DefaultAppsettingsExposeControlAndBattleRpcServicesSeparately()
    {
        using var document = Open("appsettings.json");
        var lakona = document.RootElement.GetProperty("Lakona");
        var endpoints = lakona.GetProperty("Endpoints").EnumerateArray().ToArray();

        var control = endpoints.Single(endpoint =>
            string.Equals(endpoint.GetProperty("Transport").GetString(), "websocket", StringComparison.Ordinal));
        var battle = endpoints.Single(endpoint =>
            string.Equals(endpoint.GetProperty("Transport").GetString(), "kcp", StringComparison.Ordinal));

        Assert.Equal(new[] { "login", "player" }, control.GetProperty("RpcServices").EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.Equal(new[] { "battle" }, battle.GetProperty("RpcServices").EnumerateArray().Select(item => item.GetString()).ToArray());
    }

    [Fact]
    public void DataNodeRegistersDatabaseServicesAndSqlNodeDirectory()
    {
        var services = BuildFeatureServices("appsettings.data-1.json");

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(AgarDatabaseOptions));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(AgarDatabaseConnectionFactory));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(SqlNodeDirectoryOptions));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(INodeDirectory)
            && descriptor.ImplementationType == typeof(SqlNodeDirectory));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IRouteDirectory)
            && descriptor.ImplementationType == typeof(InMemoryRouteDirectory));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<AgarDatabaseOptions>();
        var sqlOptions = provider.GetRequiredService<SqlNodeDirectoryOptions>();
        var catalog = provider.GetRequiredService<LakonaGameFeatureCatalog>();
        var routeDirectory = provider.GetRequiredService<IRouteDirectory>();

        Assert.Contains("Host=postgres", options.PostgresConnectionString);
        Assert.Contains("redis:6379", options.RedisConnectionString);
        Assert.Equal("lakona_cluster_nodes", options.NodeDirectoryTable);
        Assert.False(options.EnsureSchemaOnStartup);
        Assert.Equal(SqlNodeDirectoryDialect.Postgres, sqlOptions.Dialect);
        Assert.Equal(new[] { "database", "state-store", "matchmaking", "leaderboard" }, catalog.ActiveNames);
        Assert.IsType<InMemoryRouteDirectory>(routeDirectory);
        Assert.IsNotType<SeededRouteDirectoryClient>(routeDirectory);
    }

    [Fact]
    public void DataNodeCanEnableRuntimeSchemaCreationExplicitly()
    {
        var services = BuildFeatureServices(
            "appsettings.data-1.json",
            new Dictionary<string, string?>
            {
                ["Agar:Database:EnsureSchemaOnStartup"] = "true"
            });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<AgarDatabaseOptions>();

        Assert.True(options.EnsureSchemaOnStartup);
    }

    [Fact]
    public void AgarPostgresInitIncludesLakonaClusterNodeDirectorySchema()
    {
        var root = FindRepositoryRoot();
        var frameworkScript = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Lakona.Game.Cluster.Sql",
            "schema",
            "postgres",
            "001-lakona-cluster-nodes.sql"));
        var sampleScript = File.ReadAllText(Path.Combine(
            root,
            "samples",
            "Game.Unity.Agar",
            "infra",
            "postgres",
            "init",
            "001-lakona-cluster-nodes.sql"));

        Assert.Contains(NormalizeSql(SqlNodeDirectorySchema.CreateTableSql(SqlNodeDirectoryDialect.Postgres)), NormalizeSql(frameworkScript), StringComparison.Ordinal);
        Assert.Contains(NormalizeSql(SqlNodeDirectorySchema.CreateTableSql(SqlNodeDirectoryDialect.Postgres)), NormalizeSql(sampleScript), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GatewayNodeDoesNotRegisterDatabaseServicesOrApplicationFeatures()
    {
        var services = BuildFeatureServices("appsettings.gateway-1.json");

        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(AgarDatabaseOptions));

        await using var provider = services.BuildServiceProvider();
        var catalog = provider.GetRequiredService<LakonaGameFeatureCatalog>();

        Assert.Empty(catalog.ActiveNames);
        Assert.IsAssignableFrom<INodeDirectory>(provider.GetRequiredService<INodeDirectory>());
        Assert.IsAssignableFrom<IRouteDirectory>(provider.GetRequiredService<IRouteDirectory>());
        Assert.IsType<SeededNodeDirectoryClient>(provider.GetRequiredService<INodeDirectory>());
        Assert.IsType<SeededRouteDirectoryClient>(provider.GetRequiredService<IRouteDirectory>());
    }

    [Fact]
    public async Task BattleNodeDoesNotRegisterDatabaseServices()
    {
        var services = BuildFeatureServices("appsettings.battle-1.json");

        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(AgarDatabaseOptions));

        await using var provider = services.BuildServiceProvider();
        var catalog = provider.GetRequiredService<LakonaGameFeatureCatalog>();

        Assert.Equal(new[] { "battle-runtime" }, catalog.ActiveNames);
        Assert.IsType<SeededNodeDirectoryClient>(provider.GetRequiredService<INodeDirectory>());
        Assert.IsType<SeededRouteDirectoryClient>(provider.GetRequiredService<IRouteDirectory>());
    }

    [Fact]
    public async Task GatewayRegistrationAndBattleLookupUseDataNodeLocalRouteDirectory()
    {
        var port = GetFreePort();
        var dataRoutes = new InMemoryRouteDirectory();
        using var stopServer = new CancellationTokenSource();
        var builder = RpcServerHostBuilder.Create()
            .UseSerializer(new JsonRpcSerializer())
            .UseAcceptor(new TcpConnectionAcceptor(port, "127.0.0.1"));
        RouteDirectoryBinder.Bind(builder.ServiceRegistry, dataRoutes);
        var serverTask = builder.RunAsync(stopServer.Token).AsTask();
        await Task.Delay(100, TestContext.Current.CancellationToken);

        await using var clientFactory = new ClusterClientFactory(
            new TcpClusterTransportFactory(),
            new JsonRpcSerializer());
        var seed = $"tcp://127.0.0.1:{port}";
        var gatewayRoutes = new SeededRouteDirectoryClient(clientFactory, seed);
        var battleRoutes = new SeededRouteDirectoryClient(clientFactory, seed);
        var session = new GameSessionKey("player-1", "session-a", 3);
        var registrar = new ClientSessionRouteRegistrar(
            gatewayRoutes,
            new NodeId("gateway-1"),
            new NodeEndpoint("tcp://127.0.0.1:21002"));

        await registrar.RegisterAsync(session, TestContext.Current.CancellationToken);
        var resolvedByData = await dataRoutes.ResolveAsync(
            ClientNotificationRouteKey.FromSession(session),
            DateTimeOffset.UtcNow,
            TestContext.Current.CancellationToken);
        var resolvedByBattle = await battleRoutes.ResolveAsync(
            ClientNotificationRouteKey.FromSession(session),
            DateTimeOffset.UtcNow,
            TestContext.Current.CancellationToken);

        stopServer.Cancel();
        await Task.WhenAny(serverTask, Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));

        Assert.NotNull(resolvedByData);
        Assert.NotNull(resolvedByBattle);
        Assert.Equal(new NodeId("gateway-1"), resolvedByData.Node);
        Assert.Equal(resolvedByData.Route, resolvedByBattle.Route);
        Assert.Equal(resolvedByData.Node, resolvedByBattle.Node);
        Assert.Equal(resolvedByData.Endpoint.Address, resolvedByBattle.Endpoint.Address);
        Assert.Empty(resolvedByBattle.Endpoint.Metadata);
    }

    [Fact]
    public void ClusterEndpointDoesNotRegisterSeededDirectoriesForSelfSeed()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new Lakona.Game.Server.Configuration.LakonaGameRuntimeOptions
        {
            Cluster = new Lakona.Game.Server.Configuration.LakonaGameClusterOptions
            {
                Endpoint = "tcp://127.0.0.1:21001",
                Seeds = ["tcp://127.0.0.1:21001"]
            }
        });

        services.AddLakonaGameClusterEndpoint();

        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType == typeof(INodeDirectory)
            && descriptor.ImplementationFactory is not null);
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType == typeof(IRouteDirectory)
            && descriptor.ImplementationFactory is not null);
    }

    [Fact]
    public void RemoteNotificationExampleUsesClusterDispatcher()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "samples",
            "Game.Unity.Agar",
            "tests",
            "BusinessLogic.Tests",
            "RemoteNotificationRelayExampleTests.cs");
        var source = File.ReadAllText(path);

        Assert.Contains(nameof(ClusterClientNotificationDispatcher), source, StringComparison.Ordinal);
        Assert.DoesNotContain("GatewayProcess" + "NotificationDispatcher", source, StringComparison.Ordinal);
    }

    private static void AssertFeatureSet(JsonElement lakona, params string[] expected)
    {
        Assert.Equal(expected, lakona.GetProperty("Feature").EnumerateArray().Select(item => item.GetString()).ToArray());
    }

    private static JsonDocument Open(string fileName)
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "samples",
            "Game.Unity.Agar",
            "Server",
            "App",
            fileName);

        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static IServiceCollection BuildFeatureServices(
        string fileName,
        IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var configurationBuilder = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(FindRepositoryRoot(), "samples", "Game.Unity.Agar", "Server", "App"))
            .AddJsonFile(fileName);

        if (overrides is not null)
        {
            configurationBuilder.AddInMemoryCollection(overrides);
        }

        var configuration = configurationBuilder.Build();
        var services = new ServiceCollection();

        services.AddLakonaGame(configuration, [
            typeof(DatabaseFeature),
            typeof(StateStoreFeature),
            typeof(MatchmakingFeature),
            typeof(LeaderboardFeature),
            typeof(BattleRuntimeFeature)
        ]);

        return services;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CONTRIBUTING.md")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Could not find repository root from '{AppContext.BaseDirectory}'.");
    }

    private static string NormalizeSql(string sql)
    {
        var normalized = string.Join(
                " ",
                sql.Replace(";", string.Empty, StringComparison.Ordinal)
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Trim();
        return normalized
            .Replace("( ", "(", StringComparison.Ordinal)
            .Replace(" )", ")", StringComparison.Ordinal);
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
