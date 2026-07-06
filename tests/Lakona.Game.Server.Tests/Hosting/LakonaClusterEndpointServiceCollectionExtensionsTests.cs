using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Cluster.Rpc.MemoryPack;
using Lakona.Game.Cluster.Sql;
using Lakona.Game.Server;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Features;
using Lakona.Game.Server.Hosting;
using Lakona.Game.Server.Sessions;
using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.Json;
using Lakona.Rpc.Serializer.MemoryPack;
using Lakona.Rpc.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Lakona.Game.Server.Tests.Hosting;

[Collection("Cluster serializer registration")]
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
        var holderType = typeof(LakonaClusterEndpointServiceCollectionExtensions).Assembly.GetType(
            "Lakona.Game.Server.Hosting.LakonaClusterRpcSerializer",
            throwOnError: true)!;
        services.RemoveAll(holderType);
        services.AddSingleton(
            holderType,
            Activator.CreateInstance(holderType, ClusterRpcMemoryPack.CreateSerializer())!);
        services.AddSingleton<IRpcSerializer, JsonRpcSerializer>();
        using var provider = services.BuildServiceProvider();

        var serializer = provider.GetRequiredService<IRemoteActorSerializer>();
        var payload = serializer.Serialize(new ClusterSendReply { Status = 7 });
        var decoded = serializer.Deserialize<ClusterSendReply>(payload);
        var memoryPackDecoded = ClusterRpcMemoryPack.CreateSerializer().Deserialize<ClusterSendReply>(payload);

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
    public void AddLakonaGameClusterEndpoint_uses_configured_cluster_serializer_for_remote_actor_payloads()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lakona:Node:Id"] = "data-1",
                ["Lakona:Cluster:Endpoint"] = "tcp://127.0.0.1:21001",
                ["Lakona:Cluster:Serializer"] = "memorypack"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton(LakonaGameRuntimeOptions.FromConfiguration(configuration));

        services.AddLakonaGameClusterEndpoint();
        using var provider = services.BuildServiceProvider();

        var serializer = provider.GetRequiredService<IRemoteActorSerializer>();
        var payload = serializer.Serialize(new ClientNotificationDispatchReply { Status = 204 });
        var decoded = serializer.Deserialize<ClientNotificationDispatchReply>(payload);
        var clusterDecoded = ClusterRpcMemoryPack.CreateSerializer()
            .Deserialize<ClientNotificationDispatchReply>(payload);

        Assert.Equal(204, decoded.Status);
        Assert.Equal(204, clusterDecoded.Status);
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
    public void AddLakonaGameServer_registers_exactly_one_cluster_node_discovery_without_cluster_configuration()
    {
        var services = new ServiceCollection();

        services.AddLakonaGameServer();
        using var provider = services.BuildServiceProvider();

        var discoveries = provider.GetServices<IClusterNodeDiscovery>().ToArray();
        var discovery = Assert.Single(discoveries);
        var clusterOptions = provider.GetRequiredService<ClusterOptions>();

        Assert.IsType<ClusterNodeDiscovery>(discovery);
        Assert.Same(discovery, provider.GetRequiredService<IClusterNodeDiscovery>());
        Assert.Equal("tcp://127.0.0.1:21001", clusterOptions.AdvertisedEndpoints["cluster"]);
    }

    [Fact]
    public async Task AddLakonaGameClusterEndpoint_registers_cluster_router_dependencies()
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
        await using var provider = services.BuildServiceProvider();

        Assert.IsType<ClusterNodeMessenger>(provider.GetRequiredService<INodeMessenger>());
        Assert.IsType<ClusterRouter>(provider.GetRequiredService<IClusterRouter>());
    }

    [Fact]
    public async Task AddLakonaGameClusterEndpoint_registers_remote_actor_invoker_dependencies()
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

        services.AddLakonaGameServerActors();
        services.AddLakonaGameClusterEndpoint();
        await using var provider = services.BuildServiceProvider();

        Assert.IsType<ClusterNodeSender>(provider.GetRequiredService<IClusterNodeSender>());
        Assert.IsType<RemoteActorInvoker>(provider.GetRequiredService<IRemoteActorInvoker>());
    }

    [Fact]
    public async Task AddLakonaGameClusterEndpoint_registers_feature_message_transport()
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
        await using var provider = services.BuildServiceProvider();

        Assert.IsType<RpcFeatureMessageTransport>(provider.GetRequiredService<IFeatureMessageTransport>());
        Assert.IsType<RpcFeatureMessageSerializer>(provider.GetRequiredService<IFeatureMessageSerializer>());
        Assert.IsType<FeatureMessageBus>(provider.GetRequiredService<IFeatureMessageBus>());
    }

    [Fact]
    public async Task AddLakonaGameClusterEndpoint_supports_generated_distributed_actor_accessors()
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

        services.AddLakonaGameServerActors();
        services.AddLakonaGameClusterEndpoint();
        services.AddSingleton<GeneratedDistributedActorAccessorProbe>();
        await using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<GeneratedDistributedActorAccessorProbe>());
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

    [Fact]
    public async Task Cluster_configurator_binds_generated_actor_handlers()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new LakonaGameRuntimeOptions
        {
            Cluster = new LakonaGameClusterOptions
            {
                Endpoint = "tcp://127.0.0.1:21001",
                Serializer = "json"
            }
        });
        services.AddLakonaGameServerActors();
        services.AddLakonaGameServerSessions();
        services.AddSingleton<IClusterMessageHandler, GeneratedActorHandlerWithRouterProbe>();
        services.AddLakonaGameClusterEndpoint();

        await using var provider = services.BuildServiceProvider();
        var configurator = provider.GetServices<IRpcServerConfigurator>()
            .OfType<LakonaClusterRpcServerConfigurator>()
            .Single();
        var builder = RpcServerHostBuilder.Create();
        var context = new LakonaGameServerRpcContext(
            "cluster",
            new LakonaGameEndpointOptions { Transport = "cluster" },
            builder,
            provider,
            [],
            TestContext.Current.CancellationToken);

        configurator.Configure(context);

        Assert.True(builder.ServiceRegistry.TryGetHandler(
            ClusterProtocol.ServiceId,
            ClusterProtocol.SendMethodId,
            out _));
    }

    private sealed class GeneratedActorHandlerWithRouterProbe : IClusterMessageHandler
    {
        private readonly IClusterRouter _router;

        public GeneratedActorHandlerWithRouterProbe(IClusterRouter router)
        {
            _router = router;
        }

        public ValueTask<ClusterSendStatus> HandleAsync(
            ClusterMessage message,
            CancellationToken cancellationToken = default)
        {
            Assert.NotNull(_router);
            return new ValueTask<ClusterSendStatus>(ClusterSendStatus.RouteNotFound);
        }
    }

    private sealed class GeneratedDistributedActorAccessorProbe
    {
        public GeneratedDistributedActorAccessorProbe(
            IActorRuntime runtime,
            IRemoteActorInvoker remote,
            IRemoteActorSerializer serializer,
            RemoteActorOptions remoteOptions,
            IActorDirectory directory,
            IActorDirectoryCache directoryCache,
            LocalActorNodeIdentity localNode)
        {
            Runtime = runtime;
            Remote = remote;
            Serializer = serializer;
            RemoteOptions = remoteOptions;
            Directory = directory;
            DirectoryCache = directoryCache;
            LocalNode = localNode;
        }

        public IActorRuntime Runtime { get; }

        public IRemoteActorInvoker Remote { get; }

        public IRemoteActorSerializer Serializer { get; }

        public RemoteActorOptions RemoteOptions { get; }

        public IActorDirectory Directory { get; }

        public IActorDirectoryCache DirectoryCache { get; }

        public LocalActorNodeIdentity LocalNode { get; }
    }
}

[CollectionDefinition("Cluster serializer registration", DisableParallelization = true)]
public sealed class ClusterSerializerRegistrationCollection;
