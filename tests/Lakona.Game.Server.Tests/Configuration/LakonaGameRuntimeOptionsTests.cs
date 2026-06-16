using Microsoft.Extensions.Configuration;
using Lakona.Game.Server.Configuration;
using System.Text;
using Xunit;

namespace Lakona.Game.Server.Tests.Configuration;

public sealed class LakonaGameRuntimeOptionsTests
{
    [Fact]
    public void FromConfiguration_prefers_lakona_root()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Lakona:Node:Id"] = "gateway-1",
            ["Lakona.Game:Node:Id"] = "legacy",
            ["Lakona:Endpoints:0:Transport"] = "websocket",
            ["Lakona:Endpoints:0:Host"] = "0.0.0.0",
            ["Lakona:Endpoints:0:Port"] = "20000",
            ["Lakona:Endpoints:0:Path"] = "/ws",
            ["Lakona:Endpoints:0:RpcServices:0"] = "login",
            ["Lakona:Endpoints:0:RpcServices:1"] = "player",
            ["Lakona:Cluster:Endpoint"] = "tcp://10.0.0.2:21002",
            ["Lakona:Cluster:Seeds:0"] = "tcp://10.0.0.1:21001"
        });

        var options = LakonaGameRuntimeOptions.FromConfiguration(configuration);

        Assert.Equal("gateway-1", options.Node.Id);
        var endpoint = Assert.Single(options.Endpoints);
        Assert.Equal("websocket", endpoint.Transport);
        Assert.Equal(["login", "player"], endpoint.RpcServices);
        Assert.Equal("tcp://10.0.0.2:21002", options.Cluster!.Endpoint);
    }

    [Fact]
    public void FromConfiguration_preserves_empty_feature_array()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Lakona:Node:Id"] = "gateway-1",
            ["Lakona:Feature"] = ""
        });

        var options = LakonaGameRuntimeOptions.FromConfiguration(configuration);

        Assert.NotNull(options.Feature);
        Assert.Empty(options.Feature);
    }

    [Fact]
    public void FromConfiguration_preserves_json_empty_feature_array()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(
            """
            {
              "Lakona": {
                "Node": {
                  "Id": "gateway-1"
                },
                "Feature": []
              }
            }
            """));
        var configuration = new ConfigurationBuilder()
            .AddJsonStream(stream)
            .Build();

        var options = LakonaGameRuntimeOptions.FromConfiguration(configuration);

        Assert.NotNull(options.Feature);
        Assert.Empty(options.Feature);
    }

    [Fact]
    public void FromConfiguration_preserves_json_feature_values()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(
            """
            {
              "Lakona": {
                "Feature": [ "database" ]
              }
            }
            """));
        var configuration = new ConfigurationBuilder()
            .AddJsonStream(stream)
            .Build();

        var options = LakonaGameRuntimeOptions.FromConfiguration(configuration);

        Assert.Equal(["database"], options.Feature);
    }

    [Fact]
    public void ToClusterOptions_uses_cluster_endpoint_and_transport_keys()
    {
        var options = new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "gateway-1" },
            Cluster = new LakonaGameClusterOptions
            {
                Endpoint = "tcp://10.0.0.2:21002",
                Seeds = ["tcp://10.0.0.1:21001"]
            },
            Endpoints =
            [
                new LakonaGameEndpointOptions
                {
                    Transport = "websocket",
                    Host = "0.0.0.0",
                    AdvertisedHost = "game.example.com",
                    Port = 20000,
                    Path = "/ws"
                }
            ]
        };

        var cluster = options.ToClusterOptions();

        Assert.Equal("gateway-1", cluster.NodeId);
        Assert.Equal("tcp://10.0.0.2:21002", cluster.AdvertisedEndpoints["cluster"]);
        Assert.Equal("ws://game.example.com:20000/ws", cluster.AdvertisedEndpoints["websocket"]);
        Assert.Equal(["tcp://10.0.0.1:21001"], cluster.Bootstrap.NodeDirectoryEndpoints);
        Assert.False(cluster.AdvertisedEndpoints.ContainsKey("client"));
    }

    [Fact]
    public void ToClusterOptions_rejects_duplicate_endpoint_transports()
    {
        var options = new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "gateway-1" },
            Cluster = new LakonaGameClusterOptions { Endpoint = "tcp://10.0.0.2:21002" },
            Endpoints =
            [
                new LakonaGameEndpointOptions { Transport = "websocket", Host = "0.0.0.0", Port = 20000 },
                new LakonaGameEndpointOptions { Transport = "WebSocket", Host = "0.0.0.0", Port = 20001 }
            ]
        };

        var ex = Assert.Throws<InvalidOperationException>(() => options.ToClusterOptions());

        Assert.Contains("websocket", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromConfiguration_binds_node_endpoints_feature_and_cluster()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Lakona:Node:Id"] = "game-c",
            ["Lakona:Endpoints:0:Transport"] = "websocket",
            ["Lakona:Endpoints:0:Host"] = "0.0.0.0",
            ["Lakona:Endpoints:0:Port"] = "20000",
            ["Lakona:Endpoints:0:Path"] = "/ws",
            ["Lakona:Endpoints:1:Transport"] = "kcp",
            ["Lakona:Endpoints:1:Host"] = "0.0.0.0",
            ["Lakona:Endpoints:1:Port"] = "20001",
            ["Lakona:Feature:0"] = "battle",
            ["Lakona:Feature:1"] = "battle-settlement",
            ["Lakona:Cluster:Endpoint"] = "tcp://10.0.0.3:21003",
            ["Lakona:Cluster:Seeds:0"] = "tcp://10.0.0.1:21001"
        });

        var options = LakonaGameRuntimeOptions.FromConfiguration(configuration);

        Assert.Equal("game-c", options.Node.Id);
        Assert.Collection(
            options.Endpoints,
            endpoint =>
            {
                Assert.Equal("websocket", endpoint.Transport);
                Assert.Equal("0.0.0.0", endpoint.Host);
                Assert.Equal(20000, endpoint.Port);
                Assert.Equal("/ws", endpoint.Path);
            },
            endpoint =>
            {
                Assert.Equal("kcp", endpoint.Transport);
                Assert.Equal("0.0.0.0", endpoint.Host);
                Assert.Equal(20001, endpoint.Port);
                Assert.Equal("", endpoint.Path);
            });
        Assert.Equal(["battle", "battle-settlement"], options.Feature);
        Assert.NotNull(options.Cluster);
        Assert.Equal("tcp://10.0.0.3:21003", options.Cluster.Endpoint);
        Assert.Equal(["tcp://10.0.0.1:21001"], options.Cluster.Seeds);
    }

    [Fact]
    public void FromConfiguration_defaults_feature_to_null_and_cluster_to_null()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Lakona:Node:Id"] = "dev-1"
        });

        var options = LakonaGameRuntimeOptions.FromConfiguration(configuration);

        Assert.Equal("dev-1", options.Node.Id);
        Assert.Empty(options.Endpoints);
        Assert.Null(options.Feature);
        Assert.Null(options.Cluster);
    }

    [Fact]
    public void ToAdvertisedEndpoint_maps_websocket_to_ws()
    {
        var endpoint = new LakonaGameEndpointOptions
        {
            Transport = "websocket",
            Host = "127.0.0.1",
            Port = 20000,
            Path = "/ws"
        };

        Assert.Equal("ws://127.0.0.1:20000/ws", endpoint.ToAdvertisedEndpoint());
    }

    [Fact]
    public void ToAdvertisedEndpoint_uses_advertised_host_when_present()
    {
        var endpoint = new LakonaGameEndpointOptions
        {
            Transport = "kcp",
            Host = "0.0.0.0",
            AdvertisedHost = "game.example.com",
            Port = 20001
        };

        Assert.Equal("kcp://game.example.com:20001", endpoint.ToAdvertisedEndpoint());
    }

    [Fact]
    public void ToAdvertisedEndpoint_preserves_unknown_transport()
    {
        var endpoint = new LakonaGameEndpointOptions
        {
            Transport = "quic",
            Host = "127.0.0.1",
            Port = 20002
        };

        Assert.Equal("quic://127.0.0.1:20002", endpoint.ToAdvertisedEndpoint());
    }

    [Fact]
    public void ToAdvertisedEndpoint_normalizes_transport_case()
    {
        var endpoint = new LakonaGameEndpointOptions
        {
            Transport = "WebSocket",
            Host = "127.0.0.1",
            Port = 20003,
            Path = "/ws"
        };

        Assert.Equal("ws://127.0.0.1:20003/ws", endpoint.ToAdvertisedEndpoint());
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
