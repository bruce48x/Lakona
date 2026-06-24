using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Cluster.Sql;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Features;
using Lakona.Game.Server.Hosting;
using Lakona.Game.Server.Sessions;
using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.Json;
using Lakona.Rpc.Serializer.MemoryPack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Server.Tests.Hosting;

public sealed class LakonaClusterEndpointServiceCollectionExtensionsTests
{
    [Fact]
    public void AddLakonaGameClusterEndpoint_uses_configured_cluster_serializer()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "data-1" },
            Cluster = new LakonaGameClusterOptions
            {
                Endpoint = "tcp://127.0.0.1:21001",
                Serializer = "memorypack"
            }
        });

        services.AddLakonaGameClusterEndpoint();
        using var provider = services.BuildServiceProvider();

        Assert.IsType<MemoryPackRpcSerializer>(provider.GetRequiredService<IRpcSerializer>());
    }

    [Fact]
    public void AddLakonaGameClusterEndpoint_registers_remote_actor_serializer_adapter()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "data-1" },
            Cluster = new LakonaGameClusterOptions
            {
                Endpoint = "tcp://127.0.0.1:21001",
                Serializer = "memorypack"
            }
        });

        services.AddLakonaGameClusterEndpoint();
        services.AddSingleton<IRpcSerializer, JsonRpcSerializer>();
        using var provider = services.BuildServiceProvider();

        var serializer = provider.GetRequiredService<IRemoteActorSerializer>();
        var payload = serializer.Serialize(new ClientNotificationDispatchReply { Status = 7 });
        var decoded = serializer.Deserialize<ClientNotificationDispatchReply>(payload);
        var memoryPackDecoded = new MemoryPackRpcSerializer().Deserialize<ClientNotificationDispatchReply>(payload);

        var holderType = typeof(LakonaClusterEndpointServiceCollectionExtensions).Assembly.GetType(
            "Lakona.Game.Server.Hosting.LakonaClusterRpcSerializer",
            throwOnError: true)!;
        var holder = provider.GetRequiredService(holderType);
        var clusterSerializer = Assert.IsAssignableFrom<IRpcSerializer>(
            holderType.GetProperty("Serializer")!.GetValue(holder));

        Assert.IsType<MemoryPackRpcSerializer>(clusterSerializer);
        Assert.IsType<JsonRpcSerializer>(provider.GetRequiredService<IRpcSerializer>());
        Assert.Equal(7, decoded.Status);
        Assert.Equal(7, memoryPackDecoded.Status);
    }

    [Fact]
    public void AddLakonaGameClusterEndpoint_registers_remote_actor_serializer_adapter_for_json()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "data-1" },
            Cluster = new LakonaGameClusterOptions
            {
                Endpoint = "tcp://127.0.0.1:21001",
                Serializer = "json"
            }
        });

        services.AddLakonaGameClusterEndpoint();
        services.AddSingleton<IRpcSerializer, MemoryPackRpcSerializer>();
        using var provider = services.BuildServiceProvider();

        var serializer = provider.GetRequiredService<IRemoteActorSerializer>();
        var payload = serializer.Serialize(new ClientNotificationDispatchReply { Status = 7 });
        var decoded = serializer.Deserialize<ClientNotificationDispatchReply>(payload);
        var jsonDecoded = new JsonRpcSerializer().Deserialize<ClientNotificationDispatchReply>(payload);

        var holderType = typeof(LakonaClusterEndpointServiceCollectionExtensions).Assembly.GetType(
            "Lakona.Game.Server.Hosting.LakonaClusterRpcSerializer",
            throwOnError: true)!;
        var holder = provider.GetRequiredService(holderType);
        var clusterSerializer = Assert.IsAssignableFrom<IRpcSerializer>(
            holderType.GetProperty("Serializer")!.GetValue(holder));

        Assert.IsType<JsonRpcSerializer>(clusterSerializer);
        Assert.IsType<MemoryPackRpcSerializer>(provider.GetRequiredService<IRpcSerializer>());
        Assert.Equal(7, decoded.Status);
        Assert.Equal(7, jsonDecoded.Status);
    }

    [Fact]
    public void AddLakonaGameClusterEndpoint_replaces_existing_rpc_serializer_with_configured_cluster_serializer()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRpcSerializer, JsonRpcSerializer>();
        services.AddSingleton(new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "data-1" },
            Cluster = new LakonaGameClusterOptions
            {
                Endpoint = "tcp://127.0.0.1:21001",
                Serializer = "memorypack"
            }
        });

        services.AddLakonaGameClusterEndpoint();
        using var provider = services.BuildServiceProvider();

        Assert.IsType<MemoryPackRpcSerializer>(provider.GetRequiredService<IRpcSerializer>());
    }

    [Fact]
    public async Task AddLakonaGameClusterEndpoint_keeps_cluster_serializer_when_later_rpc_serializer_is_registered()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "data-1" },
            Cluster = new LakonaGameClusterOptions
            {
                Endpoint = "tcp://127.0.0.1:21001",
                Serializer = "memorypack"
            }
        });

        services.AddLakonaGameClusterEndpoint();
        services.AddSingleton<IRpcSerializer, JsonRpcSerializer>();
        await using var provider = services.BuildServiceProvider();

        var holderType = typeof(LakonaClusterEndpointServiceCollectionExtensions).Assembly.GetType(
            "Lakona.Game.Server.Hosting.LakonaClusterRpcSerializer",
            throwOnError: true)!;
        var holder = provider.GetRequiredService(holderType);
        var serializer = Assert.IsAssignableFrom<IRpcSerializer>(
            holderType.GetProperty("Serializer")!.GetValue(holder));

        Assert.IsType<MemoryPackRpcSerializer>(serializer);
        Assert.IsType<JsonRpcSerializer>(provider.GetRequiredService<IRpcSerializer>());
        Assert.NotNull(provider.GetRequiredService<IClusterClientFactory>());
    }

    [Fact]
    public void AddLakonaGameClusterEndpoint_registers_cluster_node_discovery()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "data-1" },
            Cluster = new LakonaGameClusterOptions
            {
                Endpoint = "tcp://127.0.0.1:21001",
                Serializer = "memorypack",
                Seeds = ["tcp://127.0.0.1:21001"]
            }
        });
        services.AddSingleton<INodeDirectory, InMemoryNodeDirectory>();

        services.AddLakonaGameClusterEndpoint();
        using var provider = services.BuildServiceProvider();

        Assert.IsType<ClusterNodeDiscovery>(provider.GetRequiredService<IClusterNodeDiscovery>());
        Assert.IsType<InMemoryRouteDirectory>(provider.GetRequiredService<IRouteDirectory>());
    }

    [Fact]
    public void AddLakonaGameClusterEndpoint_registers_sql_node_directory_from_runtime_directory_options()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:LakonaClusterPostgres"] = "Host=postgres;Database=lakona-game",
                ["Lakona:Node:Id"] = "data-1",
                ["Lakona:Cluster:Endpoint"] = "tcp://127.0.0.1:21001",
                ["Lakona:Cluster:Serializer"] = "memorypack",
                ["Lakona:Cluster:Directory:Provider"] = "postgres",
                ["Lakona:Cluster:Directory:ConnectionStringName"] = "LakonaClusterPostgres",
                ["Lakona:Cluster:Directory:NodeTable"] = "lakona_cluster_nodes"
            })
            .Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(LakonaGameRuntimeOptions.FromConfiguration(configuration));

        services.AddLakonaGameClusterEndpoint();
        using var provider = services.BuildServiceProvider();

        Assert.IsType<SqlNodeDirectory>(provider.GetRequiredService<INodeDirectory>());
        var sqlOptions = provider.GetRequiredService<SqlNodeDirectoryOptions>();
        Assert.Equal(SqlNodeDirectoryDialect.Postgres, sqlOptions.Dialect);
        Assert.Equal("lakona_cluster_nodes", sqlOptions.TableName);
    }

    [Fact]
    public void AddLakonaGame_registers_sql_node_directory_without_pre_registered_configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:LakonaClusterPostgres"] = "Host=postgres;Database=lakona-game",
                ["Lakona:Node:Id"] = "data-1",
                ["Lakona:Cluster:Endpoint"] = "tcp://127.0.0.1:21001",
                ["Lakona:Cluster:Serializer"] = "memorypack",
                ["Lakona:Cluster:Directory:Provider"] = "postgres",
                ["Lakona:Cluster:Directory:ConnectionStringName"] = "LakonaClusterPostgres"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddLakonaGame(configuration, _ => { });
        using var provider = services.BuildServiceProvider();

        Assert.IsType<SqlNodeDirectory>(provider.GetRequiredService<INodeDirectory>());
        Assert.NotNull(provider.GetRequiredService<SqlNodeDirectoryOptions>());
    }
}
