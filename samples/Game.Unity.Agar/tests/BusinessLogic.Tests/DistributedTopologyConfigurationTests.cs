using System.Text.Json;
using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Sql;
using Lakona.Game.Server.Features;
using Lakona.Game.Server.Hosting;
using Lakona.Game.Server.Sessions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Server.App.Features;
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
        Assert.False(endpoint.TryGetProperty("Path", out _));
        Assert.Equal(new[] { "battle" }, endpoint.GetProperty("RpcServices").EnumerateArray().Select(item => item.GetString()).ToArray());
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

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<AgarDatabaseOptions>();
        var sqlOptions = provider.GetRequiredService<SqlNodeDirectoryOptions>();
        var catalog = provider.GetRequiredService<LakonaGameFeatureCatalog>();

        Assert.Contains("Host=postgres", options.PostgresConnectionString);
        Assert.Contains("redis:6379", options.RedisConnectionString);
        Assert.Equal("lakona_game_cluster_nodes", options.NodeDirectoryTable);
        Assert.Equal(SqlNodeDirectoryDialect.Postgres, sqlOptions.Dialect);
        Assert.Equal(new[] { "database", "state-store", "matchmaking", "leaderboard" }, catalog.ActiveNames);
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

    private static IServiceCollection BuildFeatureServices(string fileName)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(FindRepositoryRoot(), "samples", "Game.Unity.Agar", "Server", "App"))
            .AddJsonFile(fileName)
            .Build();
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
}
