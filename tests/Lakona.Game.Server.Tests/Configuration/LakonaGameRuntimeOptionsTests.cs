using Microsoft.Extensions.Configuration;
using Lakona.Game.Server.Configuration;
using Xunit;

namespace Lakona.Game.Server.Tests.Configuration;

public sealed class LakonaGameRuntimeOptionsTests
{
    [Fact]
    public void FromConfiguration_binds_resume_and_reliable_push_limits_for_runtime_diagnostics()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Lakona:Sessions:ResumeWindowSeconds"] = "37",
            ["Lakona:ReliablePush:MaxPendingPerSession"] = "91"
        });

        var options = LakonaGameRuntimeOptions.FromConfiguration(configuration);

        Assert.Equal(TimeSpan.FromSeconds(37), options.Sessions.ResumeWindow);
        Assert.Equal(91, options.ReliablePush.MaxPendingPerSession);
    }

    [Fact]
    public void FromConfiguration_defaults_heartbeat_policy()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Lakona:Node:Id"] = "gateway-1"
        });

        var options = LakonaGameRuntimeOptions.FromConfiguration(configuration);

        Assert.Equal(TimeSpan.FromSeconds(15), options.Heartbeat.Interval);
        Assert.Equal(TimeSpan.FromSeconds(45), options.Heartbeat.Timeout);
    }

    [Fact]
    public void FromConfiguration_defaults_management_listener_and_health_policy()
    {
        var options = LakonaGameRuntimeOptions.FromConfiguration(new ConfigurationBuilder().Build());

        Assert.False(options.Health.Enabled);
        Assert.True(options.Health.RequireLoopback);
        Assert.Equal("127.0.0.1", options.Management.Http.Host);
        Assert.Equal(20080, options.Management.Http.Port);
    }

    [Fact]
    public void FromConfiguration_binds_management_listener_and_health_policy()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Lakona:Health:Enabled"] = "true",
            ["Lakona:Health:RequireLoopback"] = "false",
            ["Lakona:Management:Http:Host"] = "0.0.0.0",
            ["Lakona:Management:Http:Port"] = "20180"
        });

        var options = LakonaGameRuntimeOptions.FromConfiguration(configuration);

        Assert.True(options.Health.Enabled);
        Assert.False(options.Health.RequireLoopback);
        Assert.Equal("0.0.0.0", options.Management.Http.Host);
        Assert.Equal(20180, options.Management.Http.Port);
    }

    [Fact]
    public void FromConfiguration_binds_multiple_application_http_listeners()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Lakona:Http:Listeners:0:Id"] = "operations",
            ["Lakona:Http:Listeners:0:Host"] = "10.0.0.10",
            ["Lakona:Http:Listeners:0:Port"] = "21000",
            ["Lakona:Http:Listeners:0:Services:0"] = "operations",
            ["Lakona:Http:Listeners:0:MaximumBodyBytes"] = "1048576",
            ["Lakona:Http:Listeners:0:RequestTimeoutSeconds"] = "30",
            ["Lakona:Http:Listeners:1:Id"] = "payments",
            ["Lakona:Http:Listeners:1:Host"] = "0.0.0.0",
            ["Lakona:Http:Listeners:1:Port"] = "21001",
            ["Lakona:Http:Listeners:1:Services:0"] = "payment-webhooks",
            ["Lakona:Http:Listeners:1:MaximumBodyBytes"] = "262144",
            ["Lakona:Http:Listeners:1:RequestTimeoutSeconds"] = "15"
        });

        var options = LakonaGameRuntimeOptions.FromConfiguration(configuration);

        Assert.Collection(
            options.Http.Listeners,
            listener =>
            {
                Assert.Equal("operations", listener.Id);
                Assert.Equal("10.0.0.10", listener.Host);
                Assert.Equal(21000, listener.Port);
                Assert.Equal(["operations"], listener.Services);
                Assert.Equal(1048576, listener.MaximumBodyBytes);
                Assert.Equal(30, listener.RequestTimeoutSeconds);
            },
            listener =>
            {
                Assert.Equal("payments", listener.Id);
                Assert.Equal("0.0.0.0", listener.Host);
                Assert.Equal(21001, listener.Port);
                Assert.Equal(["payment-webhooks"], listener.Services);
                Assert.Equal(262144, listener.MaximumBodyBytes);
                Assert.Equal(15, listener.RequestTimeoutSeconds);
            });
    }

    [Fact]
    public void FromConfiguration_binds_json_application_http_listeners()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Lakona:Http:Listeners"] =
                """
                [
                  {
                    "id": "payments",
                    "host": "127.0.0.1",
                    "port": 21001,
                    "services": [ "payment-webhooks" ],
                    "maximumBodyBytes": 262144,
                    "requestTimeoutSeconds": 15
                  }
                ]
                """
        });

        var options = LakonaGameRuntimeOptions.FromConfiguration(configuration);

        var listener = Assert.Single(options.Http.Listeners);
        Assert.Equal("payments", listener.Id);
        Assert.Equal(["payment-webhooks"], listener.Services);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FromConfiguration_rejects_removed_application_http_exposure(bool json)
    {
        var values = json
            ? new Dictionary<string, string?>
            {
                ["Lakona:Http:Listeners"] =
                    """
                    [
                      {
                        "id": "payments",
                        "host": "127.0.0.1",
                        "port": 21001,
                        "exposure": "Public",
                        "services": [ "payment-webhooks" ]
                      }
                    ]
                    """
            }
            : new Dictionary<string, string?>
            {
                ["Lakona:Http:Listeners:0:Id"] = "payments",
                ["Lakona:Http:Listeners:0:Host"] = "127.0.0.1",
                ["Lakona:Http:Listeners:0:Port"] = "21001",
                ["Lakona:Http:Listeners:0:Exposure"] = "Public",
                ["Lakona:Http:Listeners:0:Services:0"] = "payment-webhooks"
            };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            LakonaGameRuntimeOptions.FromConfiguration(BuildConfiguration(values)));

        Assert.Contains(
            "Exposure was removed",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("invalid-port")]
    [InlineData("empty-service")]
    public void FromConfiguration_rejects_invalid_application_http_listeners(string scenario)
    {
        var values = new Dictionary<string, string?>
        {
            ["Lakona:Http:Listeners:0:Id"] = "payments",
            ["Lakona:Http:Listeners:0:Host"] = "127.0.0.1",
            ["Lakona:Http:Listeners:0:Port"] = "21001",
            ["Lakona:Http:Listeners:0:Services:0"] = "payment-webhooks"
        };

        if (scenario == "duplicate")
        {
            values["Lakona:Http:Listeners:1:Id"] = "PAYMENTS";
            values["Lakona:Http:Listeners:1:Host"] = "127.0.0.1";
            values["Lakona:Http:Listeners:1:Port"] = "21002";
            values["Lakona:Http:Listeners:1:Services:0"] = "operations";
        }
        else if (scenario == "invalid-port")
        {
            values["Lakona:Http:Listeners:0:Port"] = "0";
        }
        else
        {
            values["Lakona:Http:Listeners:0:Services:0"] = "";
        }

        var exception = Assert.Throws<InvalidOperationException>(() =>
            LakonaGameRuntimeOptions.FromConfiguration(BuildConfiguration(values)));

        Assert.Contains("Lakona:Http:Listeners", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FromConfiguration_rejects_removed_health_http_section()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Lakona:Health:Http:Port"] = "20180"
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            LakonaGameRuntimeOptions.FromConfiguration(configuration));

        Assert.Contains("Lakona:Health:Http was removed", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Lakona:Management:Http", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FromConfiguration_binds_actor_hosts()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Lakona:ActorHosts:0"] = "room",
            ["Lakona:ActorHosts:1"] = "user"
        });

        var options = LakonaGameRuntimeOptions.FromConfiguration(configuration);

        Assert.Equal(["room", "user"], options.ActorHosts);
    }

    [Fact]
    public void FromConfiguration_treats_omitted_actor_hosts_as_empty()
    {
        var options = LakonaGameRuntimeOptions.FromConfiguration(new ConfigurationBuilder().Build());

        Assert.Empty(options.ActorHosts);
    }

    [Fact]
    public void FromConfiguration_rejects_removed_startup_actor_array()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Lakona:StartupActors:0"] = "matchmaking",
            ["Lakona:StartupActors:1"] = "leaderboard"
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            LakonaGameRuntimeOptions.FromConfiguration(configuration));
        Assert.Contains("RegisterStartup<TActor, TKey>", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FromConfiguration_rejects_removed_startup_actor_json()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Lakona:StartupActors"] =
                """
                [
                  {
                    "Name": "matchmaking",
                    "Options": {
                      "queue": "ranked"
                    }
                  }
                ]
                """
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            LakonaGameRuntimeOptions.FromConfiguration(configuration));
        Assert.Contains("Lakona:ActorHosts", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FromConfiguration_binds_heartbeat_policy()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Lakona:Heartbeat:Interval"] = "00:00:05",
            ["Lakona:Heartbeat:Timeout"] = "00:00:20"
        });

        var options = LakonaGameRuntimeOptions.FromConfiguration(configuration);

        Assert.Equal(TimeSpan.FromSeconds(5), options.Heartbeat.Interval);
        Assert.Equal(TimeSpan.FromSeconds(20), options.Heartbeat.Timeout);
    }

    [Fact]
    public void FromConfiguration_ignores_legacy_lakona_game_heartbeat_root()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Lakona.Game:Heartbeat:Interval"] = "00:00:01",
            ["Lakona.Game:Heartbeat:Timeout"] = "00:00:02"
        });

        var options = LakonaGameRuntimeOptions.FromConfiguration(configuration);

        Assert.Equal(TimeSpan.FromSeconds(15), options.Heartbeat.Interval);
        Assert.Equal(TimeSpan.FromSeconds(45), options.Heartbeat.Timeout);
    }

    [Fact]
    public void FromConfiguration_ignores_legacy_lakona_game_root_when_lakona_root_exists()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Lakona:Node:Id"] = "gateway-1",
            ["Lakona.Game:Node:Id"] = "legacy",
            ["Lakona:Endpoints:0:Transport"] = "websocket",
            ["Lakona:Endpoints:0:Serializer"] = "json",
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
        Assert.Equal("json", endpoint.Serializer);
        Assert.Equal(["login", "player"], endpoint.RpcServices);
        Assert.Equal("tcp://10.0.0.2:21002", options.Cluster!.Endpoint);
    }

    [Fact]
    public void FromConfiguration_uses_defaults_when_only_legacy_lakona_game_root_exists()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Lakona.Game:Node:Id"] = "legacy",
            ["Lakona.Game:Endpoints:0:Transport"] = "websocket",
            ["Lakona.Game:Cluster:Endpoint"] = "tcp://10.0.0.2:21002"
        });

        var options = LakonaGameRuntimeOptions.FromConfiguration(configuration);

        Assert.Equal("dev-1", options.Node.Id);
        Assert.Empty(options.Endpoints);
        Assert.NotNull(options.Cluster);
        Assert.Equal("tcp://127.0.0.1:21001", options.Cluster.Endpoint);
    }

    [Fact]
    public void FromConfiguration_preserves_explicit_blank_cluster_section()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Lakona:Node:Id"] = "gateway-1",
            ["Lakona:Cluster"] = ""
        });

        var options = LakonaGameRuntimeOptions.FromConfiguration(configuration);

        Assert.Equal("", options.Cluster.Endpoint);
    }

    [Fact]
    public void Runtime_options_public_surface_does_not_expose_compatibility_cluster_endpoint()
    {
        var propertyNames = typeof(LakonaGameRuntimeOptions)
            .GetProperties()
            .Select(static property => property.Name)
            .ToArray();

        Assert.DoesNotContain("ClusterEndpoint", propertyNames);
    }

    [Fact]
    public void FromConfiguration_binds_json_string_endpoints_and_rpc_services()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Lakona:Endpoints"] =
                """
                [
                  {
                    "transport": "websocket",
                    "serializer": "memorypack",
                    "host": "0.0.0.0",
                    "advertisedHost": "gateway-1",
                    "port": 20000,
                    "path": "/ws",
                    "rpcServices": [ "login", "player" ]
                  }
                ]
                """
        });

        var options = LakonaGameRuntimeOptions.FromConfiguration(configuration);

        var endpoint = Assert.Single(options.Endpoints);
        Assert.Equal("websocket", endpoint.Transport);
        Assert.Equal("memorypack", endpoint.Serializer);
        Assert.Equal("0.0.0.0", endpoint.Host);
        Assert.Equal("gateway-1", endpoint.AdvertisedHost);
        Assert.Equal(20000, endpoint.Port);
        Assert.Equal("/ws", endpoint.Path);
        Assert.Equal(["login", "player"], endpoint.RpcServices);
    }

    [Fact]
    public void FromConfiguration_binds_json_string_cluster_seeds()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Lakona:Cluster:Endpoint"] = "tcp://10.0.0.2:21002",
            ["Lakona:Cluster:Seeds"] = """["tcp://10.0.0.1:21001"]"""
        });

        var options = LakonaGameRuntimeOptions.FromConfiguration(configuration);

        Assert.Equal(["tcp://10.0.0.1:21001"], options.Cluster!.Seeds);
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
        Assert.False(cluster.AdvertisedEndpoints.ContainsKey("client"));
    }

    [Fact]
    public void ToClusterOptions_rejects_duplicate_endpoint_transports()
    {
        var options = new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "gateway-1" },
            Cluster = new LakonaGameClusterOptions
            {
                Endpoint = "tcp://10.0.0.2:21002",
            },
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
    public void FromConfiguration_binds_node_endpoints_actor_hosts_and_cluster()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Lakona:Node:Id"] = "game-c",
            ["Lakona:Endpoints:0:Transport"] = "websocket",
            ["Lakona:Endpoints:0:Serializer"] = "json",
            ["Lakona:Endpoints:0:Host"] = "0.0.0.0",
            ["Lakona:Endpoints:0:Port"] = "20000",
            ["Lakona:Endpoints:0:Path"] = "/ws",
            ["Lakona:Endpoints:1:Transport"] = "kcp",
            ["Lakona:Endpoints:1:Serializer"] = "memorypack",
            ["Lakona:Endpoints:1:Host"] = "0.0.0.0",
            ["Lakona:Endpoints:1:Port"] = "20001",
            ["Lakona:ActorHosts:0"] = "room",
            ["Lakona:ActorHosts:1"] = "matchmaking",
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
                Assert.Equal("json", endpoint.Serializer);
                Assert.Equal("0.0.0.0", endpoint.Host);
                Assert.Equal(20000, endpoint.Port);
                Assert.Equal("/ws", endpoint.Path);
            },
            endpoint =>
            {
                Assert.Equal("kcp", endpoint.Transport);
                Assert.Equal("memorypack", endpoint.Serializer);
                Assert.Equal("0.0.0.0", endpoint.Host);
                Assert.Equal(20001, endpoint.Port);
                Assert.Equal("", endpoint.Path);
            });
        Assert.Equal(["room", "matchmaking"], options.ActorHosts);
        Assert.NotNull(options.Cluster);
        Assert.Equal("tcp://10.0.0.3:21003", options.Cluster.Endpoint);
        Assert.Equal(["tcp://10.0.0.1:21001"], options.Cluster.Seeds);
    }

    [Fact]
    public void FromConfiguration_defaults_actor_hosts_to_empty_and_cluster_to_defaults()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Lakona:Node:Id"] = "dev-1"
        });

        var options = LakonaGameRuntimeOptions.FromConfiguration(configuration);

        Assert.Equal("dev-1", options.Node.Id);
        Assert.Empty(options.Endpoints);
        Assert.Empty(options.ActorHosts);
        Assert.NotNull(options.Cluster);
        Assert.Equal("tcp://127.0.0.1:21001", options.Cluster.Endpoint);
        Assert.Empty(options.Cluster.Seeds);
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
