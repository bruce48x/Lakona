using System.Text.Json;
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
