using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Lakona.Game.Server.Configuration;
using Xunit;

namespace Agar.Unity.Tests;

public sealed class GatewayConfigurationTests
{
    [Fact]
    public void AppsettingsUsesCanonicalLakonaEndpointConfiguration()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "samples",
            "Game.Unity.Agar",
            "Server",
            "App",
            "appsettings.json");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        Assert.False(root.TryGetProperty("ControlPlane", out _));
        Assert.False(root.TryGetProperty("Realtime", out _));
        Assert.False(root.TryGetProperty("Gateway", out _));
        Assert.False(root.TryGetProperty("Hotfix", out _));
        Assert.False(root.TryGetProperty("Deployment", out _));
        Assert.False(root.TryGetProperty("Services", out _));
        Assert.False(root.TryGetProperty("Cluster", out _));
        Assert.False(root.TryGetProperty("Lakona.Game", out _));

        var lakona = root.GetProperty("Lakona");
        Assert.Equal("gateway-1", lakona.GetProperty("Node").GetProperty("Id").GetString());

        var endpoints = lakona.GetProperty("Endpoints").EnumerateArray().ToArray();
        Assert.Equal(2, endpoints.Length);

        var control = endpoints.Single(endpoint =>
            string.Equals(endpoint.GetProperty("Transport").GetString(), "websocket", StringComparison.Ordinal));
        Assert.Equal("memorypack", control.GetProperty("Serializer").GetString());
        Assert.Equal("127.0.0.1", control.GetProperty("Host").GetString());
        Assert.Equal(20000, control.GetProperty("Port").GetInt32());
        Assert.Equal("/ws", control.GetProperty("Path").GetString());
        Assert.Equal(new[] { "login", "player" }, control.GetProperty("RpcServices").EnumerateArray().Select(item => item.GetString()).ToArray());

        var realtime = endpoints.Single(endpoint =>
            string.Equals(endpoint.GetProperty("Transport").GetString(), "kcp", StringComparison.Ordinal));
        Assert.Equal("memorypack", realtime.GetProperty("Serializer").GetString());
        Assert.Equal("127.0.0.1", realtime.GetProperty("Host").GetString());
        Assert.Equal(20001, realtime.GetProperty("Port").GetInt32());
        Assert.False(realtime.TryGetProperty("Path", out _));
        Assert.Equal(new[] { "battle" }, realtime.GetProperty("RpcServices").EnumerateArray().Select(item => item.GetString()).ToArray());
    }

    [Fact]
    public void FromConfigurationBindsEndpointLocalSerializer()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lakona:Endpoints:0:Transport"] = "kcp",
                ["Lakona:Endpoints:0:Serializer"] = "memorypack",
                ["Lakona:Endpoints:0:Host"] = "0.0.0.0",
                ["Lakona:Endpoints:0:Port"] = "20001"
            })
            .Build();

        var endpoint = Assert.Single(LakonaGameRuntimeOptions.FromConfiguration(configuration).Endpoints);

        Assert.Equal("kcp", endpoint.Transport);
        Assert.Equal("memorypack", endpoint.Serializer);
        Assert.Equal("0.0.0.0", endpoint.Host);
        Assert.Equal(20001, endpoint.Port);
        Assert.Equal("", endpoint.Path);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CONTRIBUTING.md")) &&
                Directory.Exists(Path.Combine(directory.FullName, "samples", "Game.Unity.Agar")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Could not find repository root from '{AppContext.BaseDirectory}'.");
    }
}
