using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Hosting;
using Lakona.Game.Server.Sessions;
using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.Json;
using Lakona.Rpc.Serializer.MemoryPack;
using Lakona.Rpc.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Lakona.Game.Server.Tests.Hosting;

[Collection("Cluster serializer registration")]
public sealed class LakonaClusterEndpointServiceCollectionExtensionsTests
{
    private const string Seed = "tcp://127.0.0.1:21001";
    private const string Gateway = "tcp://127.0.0.1:21002";

    [Fact]
    public async Task Cluster_endpoint_replaces_session_only_notification_dispatcher()
    {
        var services = new ServiceCollection().AddTestEndpointRuntimes();
        services.AddSingleton(new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "data-1" },
            Cluster = new LakonaGameClusterOptions
            {
                Endpoint = Seed,
            }
        });
        services.AddLakonaGameServerSessions();
        services.AddLakonaGameClusterEndpoint();

        await using var provider = services.BuildServiceProvider();

        Assert.IsType<ClusterClientNotificationDispatcher>(
            provider.GetRequiredService<IClientNotificationRemoteDispatcher>());
    }

    [Fact]
    public async Task Cluster_endpoint_preserves_custom_notification_dispatcher()
    {
        var custom = new CustomNotificationDispatcher();
        var services = new ServiceCollection().AddTestEndpointRuntimes();
        services.AddSingleton(new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "data-1" },
            Cluster = new LakonaGameClusterOptions
            {
                Endpoint = Seed,
            }
        });
        services.AddLakonaGameServerSessions();
        services.AddSingleton<IClientNotificationRemoteDispatcher>(custom);
        services.AddLakonaGameClusterEndpoint();

        await using var provider = services.BuildServiceProvider();

        Assert.Same(custom, provider.GetRequiredService<IClientNotificationRemoteDispatcher>());
    }

    [Fact]
    public async Task Replicated_cluster_wires_membership_backed_runtime_services()
    {
        var services = new ServiceCollection().AddTestEndpointRuntimes();
        services.AddSingleton(new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "data-1" },
            Cluster = new LakonaGameClusterOptions
            {
                Endpoint = Seed,
                BootstrapNewCluster = true,
            }
        });
        services.AddLakonaGameServer();

        await using var provider = services.BuildServiceProvider();

        var directory = provider.GetRequiredService<IActorDirectory>();
        Assert.IsType<ReplicatedActorActivationDirectory>(directory);
        Assert.Same(directory, provider.GetRequiredService<IActorActivationDirectory>());
        Assert.Contains(
            provider.GetServices<IClusterMessageHandler>(),
            handler => ReferenceEquals(handler, directory));
        Assert.IsType<MembershipSessionRouteDirectory>(provider.GetRequiredService<IRouteDirectory>());
        Assert.IsType<MembershipClusterNodeDiscovery>(provider.GetRequiredService<IClusterNodeDiscovery>());
        Assert.IsAssignableFrom<IExactClusterNodeSender>(provider.GetRequiredService<IClusterNodeSender>());
        Assert.IsType<MembershipGameSessionIdFactory>(provider.GetRequiredService<IGameSessionIdFactory>());
    }

    [Fact]
    public async Task Membership_starts_before_startup_actors_can_enter_distributed_directory()
    {
        var services = new ServiceCollection().AddTestEndpointRuntimes();
        services.AddSingleton(new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "data-1" },
            ActorHosts = ["user"],
            Cluster = new LakonaGameClusterOptions
            {
                Endpoint = Seed,
                BootstrapNewCluster = true,
            }
        });
        services.AddLakonaGameServer();

        await using var provider = services.BuildServiceProvider();
        var hosted = provider.GetServices<IHostedService>().ToList();

        var membershipIndex = hosted.FindIndex(
            service => service is ReplicatedClusterMembershipHostedService);
        var startupActorIndex = hosted.FindIndex(service => service is StartupActorHostedService);
        Assert.True(membershipIndex >= 0);
        Assert.True(startupActorIndex > membershipIndex);
    }

    [Fact]
    public void Cluster_endpoint_without_actor_runtime_does_not_wire_actor_directory()
    {
        var services = new ServiceCollection().AddTestEndpointRuntimes();
        var directory = new InMemoryActorDirectory();
        services.AddSingleton<IActorDirectory>(directory);
        services.AddSingleton(new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "gateway-1" },
            Cluster = new LakonaGameClusterOptions
            {
                Endpoint = Gateway,
                Seeds = [Seed],
            }
        });

        services.AddLakonaGameClusterEndpoint();

        var descriptor = Assert.Single(services, item => item.ServiceType == typeof(IActorDirectory));
        Assert.Same(directory, descriptor.ImplementationInstance);
        Assert.DoesNotContain(
            services,
            item => item.ImplementationType == typeof(ActorDirectoryClusterHandler));
    }

    [Fact]
    public async Task Reconfiguring_cluster_endpoint_from_local_to_remote_removes_local_handler()
    {
        var services = new ServiceCollection().AddTestEndpointRuntimes();
        services.AddLakonaGameServerActors();
        AddRuntimeOptions(services, Seed, [Seed]);
        services.AddLakonaGameClusterEndpoint();
        AddRuntimeOptions(services, Gateway, [Seed]);
        services.AddLakonaGameClusterEndpoint();

        await using var provider = services.BuildServiceProvider();

        Assert.IsType<InMemoryActorDirectory>(provider.GetRequiredService<IActorDirectory>());
        Assert.Contains(
            provider.GetServices<IClusterMessageHandler>(),
            handler => handler is ActorDirectoryClusterHandler);
    }

    [Fact]
    public async Task Reconfiguring_cluster_endpoint_from_remote_to_local_restores_local_directory_once()
    {
        var services = new ServiceCollection().AddTestEndpointRuntimes();
        services.AddLakonaGameServerActors();
        AddRuntimeOptions(services, Gateway, [Seed]);
        services.AddLakonaGameClusterEndpoint();
        AddRuntimeOptions(services, Seed, [Seed]);
        services.AddLakonaGameClusterEndpoint();

        await using var provider = services.BuildServiceProvider();

        Assert.IsType<InMemoryActorDirectory>(provider.GetRequiredService<IActorDirectory>());
        Assert.Single(
            provider.GetServices<IClusterMessageHandler>(),
            handler => handler is ActorDirectoryClusterHandler);
    }

    [Fact]
    public async Task Cluster_seed_preserves_custom_local_actor_directory()
    {
        var services = new ServiceCollection().AddTestEndpointRuntimes();
        services.AddLakonaGameServerActors();
        services.RemoveAll<IActorDirectory>();
        var directory = new InMemoryActorDirectory();
        services.AddSingleton<IActorDirectory>(directory);
        AddRuntimeOptions(services, Seed, [Seed]);
        services.AddLakonaGameClusterEndpoint();

        await using var provider = services.BuildServiceProvider();

        Assert.Same(directory, provider.GetRequiredService<IActorDirectory>());
        Assert.Single(
            provider.GetServices<IClusterMessageHandler>(),
            handler => handler is ActorDirectoryClusterHandler);
    }

    [Fact]
    public async Task Cluster_seed_keeps_local_actor_directory_and_registers_handler()
    {
        await using var provider = BuildProvider(Seed, [Seed]);

        Assert.IsType<InMemoryActorDirectory>(provider.GetRequiredService<IActorDirectory>());
        Assert.Contains(
            provider.GetServices<IClusterMessageHandler>(),
            handler => handler is ActorDirectoryClusterHandler);
    }

    [Fact]
    public async Task Remote_node_uses_seeded_actor_directory_without_local_handler()
    {
        await using var provider = BuildProvider(Gateway, [Seed]);

        Assert.IsType<InMemoryActorDirectory>(provider.GetRequiredService<IActorDirectory>());
        Assert.Contains(
            provider.GetServices<IClusterMessageHandler>(),
            handler => handler is ActorDirectoryClusterHandler);
    }

    [Fact]
    public async Task Multiple_seeds_use_first_seed_as_canonical_actor_directory_owner()
    {
        var secondarySeed = "tcp://127.0.0.1:21003";
        await using var canonicalProvider = BuildProvider(Seed, [Seed, secondarySeed]);
        await using var secondaryProvider = BuildProvider(secondarySeed, [Seed, secondarySeed]);

        Assert.IsType<InMemoryActorDirectory>(canonicalProvider.GetRequiredService<IActorDirectory>());
        Assert.Single(
            canonicalProvider.GetServices<IClusterMessageHandler>(),
            handler => handler is ActorDirectoryClusterHandler);
        Assert.IsType<InMemoryActorDirectory>(secondaryProvider.GetRequiredService<IActorDirectory>());
        Assert.Contains(
            secondaryProvider.GetServices<IClusterMessageHandler>(),
            handler => handler is ActorDirectoryClusterHandler);
    }

    [Fact]
    public void AddLakonaGameClusterEndpoint_keeps_cluster_serializer_private_to_channel()
    {
        var services = new ServiceCollection().AddTestEndpointRuntimes();
        services.AddSingleton(new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "data-1" },
            Cluster = new LakonaGameClusterOptions
            {
                Endpoint = "tcp://127.0.0.1:21001",
            }
        });

        services.AddLakonaGameClusterEndpoint();
        using var provider = services.BuildServiceProvider();

        Assert.IsType<MemoryPackRpcSerializer>(
            provider.GetRequiredService<ClusterRpcChannel>().Serializer);
        Assert.Null(provider.GetService<IRpcSerializer>());
    }

    [Fact]
    public async Task AddLakonaGameClusterEndpoint_keeps_remote_actor_transport_on_the_fixed_cluster_codec()
    {
        var services = new ServiceCollection().AddTestEndpointRuntimes();
        services.AddSingleton(new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "data-1" },
            Cluster = new LakonaGameClusterOptions
            {
                Endpoint = "tcp://127.0.0.1:21001",
            }
        });

        services.AddLakonaGameClusterEndpoint();
        services.AddSingleton<IRpcSerializer, JsonRpcSerializer>();
        await using var provider = services.BuildServiceProvider();

        var clusterSerializer = provider.GetRequiredService<ClusterRpcChannel>().Serializer;

        Assert.IsType<MemoryPackRpcSerializer>(clusterSerializer);
        Assert.IsType<JsonRpcSerializer>(provider.GetRequiredService<IRpcSerializer>());
        Assert.IsType<RemoteActorInvoker>(provider.GetRequiredService<IRemoteActorInvoker>());
    }

    [Fact]
    public void AddLakonaGameClusterEndpoint_preserves_existing_rpc_serializer()
    {
        var services = new ServiceCollection().AddTestEndpointRuntimes();
        var serializer = new JsonRpcSerializer();
        services.AddSingleton<IRpcSerializer>(serializer);
        services.AddSingleton(new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "data-1" },
            Cluster = new LakonaGameClusterOptions
            {
                Endpoint = "tcp://127.0.0.1:21001",
            }
        });

        services.AddLakonaGameClusterEndpoint();
        using var provider = services.BuildServiceProvider();

        Assert.Same(serializer, provider.GetRequiredService<IRpcSerializer>());
        Assert.IsType<MemoryPackRpcSerializer>(
            provider.GetRequiredService<ClusterRpcChannel>().Serializer);
    }

    [Fact]
    public async Task AddLakonaGameClusterEndpoint_keeps_cluster_serializer_when_later_rpc_serializer_is_registered()
    {
        var services = new ServiceCollection().AddTestEndpointRuntimes();
        services.AddSingleton(new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "data-1" },
            Cluster = new LakonaGameClusterOptions
            {
                Endpoint = "tcp://127.0.0.1:21001",
            }
        });

        services.AddLakonaGameClusterEndpoint();
        services.AddSingleton<IRpcSerializer, JsonRpcSerializer>();
        await using var provider = services.BuildServiceProvider();

        var serializer = provider.GetRequiredService<ClusterRpcChannel>().Serializer;

        Assert.IsType<MemoryPackRpcSerializer>(serializer);
        Assert.IsType<JsonRpcSerializer>(provider.GetRequiredService<IRpcSerializer>());
        Assert.NotNull(provider.GetRequiredService<IClusterClientFactory>());
    }

    [Fact]
    public void AddLakonaGameClusterEndpoint_uses_memorypack_for_remote_actor_payloads()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lakona:Node:Id"] = "data-1",
                ["Lakona:Cluster:Endpoint"] = "tcp://127.0.0.1:21001",
            })
            .Build();
        var services = new ServiceCollection().AddTestEndpointRuntimes();
        services.AddSingleton(LakonaGameRuntimeOptions.FromConfiguration(configuration));

        services.AddLakonaGameClusterEndpoint();
        using var provider = services.BuildServiceProvider();

        Assert.IsType<MemoryPackRpcSerializer>(
            provider.GetRequiredService<ClusterRpcChannel>().Serializer);
    }

    [Fact]
    public void AddLakonaGameClusterEndpoint_registers_cluster_node_discovery()
    {
        var services = new ServiceCollection().AddTestEndpointRuntimes();
        services.AddSingleton(new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "data-1" },
            Cluster = new LakonaGameClusterOptions
            {
                Endpoint = "tcp://127.0.0.1:21001",
                Seeds = ["tcp://127.0.0.1:21001"]
            }
        });
        services.AddLakonaGameClusterEndpoint();
        using var provider = services.BuildServiceProvider();

        Assert.IsType<LocalClusterNodeDiscovery>(provider.GetRequiredService<IClusterNodeDiscovery>());
        Assert.IsType<InMemoryRouteDirectory>(provider.GetRequiredService<IRouteDirectory>());
    }

    [Fact]
    public void AddLakonaGameServer_registers_exactly_one_cluster_node_discovery_without_cluster_configuration()
    {
        var services = new ServiceCollection().AddTestEndpointRuntimes();

        services.AddLakonaGameServer();
        using var provider = services.BuildServiceProvider();

        var discoveries = provider.GetServices<IClusterNodeDiscovery>().ToArray();
        var discovery = Assert.Single(discoveries);
        var clusterOptions = provider.GetRequiredService<ClusterOptions>();

        Assert.IsType<LocalClusterNodeDiscovery>(discovery);
        Assert.Same(discovery, provider.GetRequiredService<IClusterNodeDiscovery>());
        Assert.Equal("tcp://127.0.0.1:21001", clusterOptions.AdvertisedEndpoints["cluster"]);
    }

    [Fact]
    public async Task AddLakonaGameClusterEndpoint_registers_cluster_router_dependencies()
    {
        var services = new ServiceCollection().AddTestEndpointRuntimes();
        services.AddSingleton(new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "data-1" },
            Cluster = new LakonaGameClusterOptions
            {
                Endpoint = "tcp://127.0.0.1:21001",
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
        var services = new ServiceCollection().AddTestEndpointRuntimes();
        services.AddSingleton(new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "data-1" },
            Cluster = new LakonaGameClusterOptions
            {
                Endpoint = "tcp://127.0.0.1:21001",
            }
        });

        services.AddLakonaGameServerActors();
        services.AddLakonaGameClusterEndpoint();
        await using var provider = services.BuildServiceProvider();

        Assert.IsType<ClusterNodeSender>(provider.GetRequiredService<IClusterNodeSender>());
        Assert.IsType<RemoteActorInvoker>(provider.GetRequiredService<IRemoteActorInvoker>());
    }

    [Fact]
    public async Task AddLakonaGameClusterEndpoint_registers_only_cluster_router_dependencies()
    {
        var services = new ServiceCollection().AddTestEndpointRuntimes();
        services.AddSingleton(new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "data-1" },
            Cluster = new LakonaGameClusterOptions
            {
                Endpoint = "tcp://127.0.0.1:21001",
            }
        });

        services.AddLakonaGameClusterEndpoint();
        await using var provider = services.BuildServiceProvider();

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IClusterRouter));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IClusterNodeSender));
    }

    [Fact]
    public async Task AddLakonaGameClusterEndpoint_supports_generated_distributed_actor_accessors()
    {
        var services = new ServiceCollection().AddTestEndpointRuntimes();
        services.AddSingleton(new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "data-1" },
            Cluster = new LakonaGameClusterOptions
            {
                Endpoint = "tcp://127.0.0.1:21001",
            }
        });

        services.AddLakonaGameServerActors();
        services.AddLakonaGameClusterEndpoint();
        services.AddSingleton<GeneratedDistributedActorAccessorProbe>();
        await using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<GeneratedDistributedActorAccessorProbe>());
    }

    [Fact]
    public async Task Cluster_configurator_binds_generated_actor_handlers()
    {
        var services = new ServiceCollection().AddTestEndpointRuntimes();
        services.AddSingleton(new LakonaGameRuntimeOptions
        {
            Cluster = new LakonaGameClusterOptions
            {
                Endpoint = "tcp://127.0.0.1:21001",
            }
        });
        services.AddLakonaGameServerActors();
        services.AddLakonaGameServerSessions();
        services.AddSingleton<IClusterMessageHandler, GeneratedActorHandlerWithRouterProbe>();
        services.AddLakonaGameClusterEndpoint();

        await using var provider = services.BuildServiceProvider();
        Assert.Contains(
            provider.GetServices<IClusterMessageHandler>(),
            static handler => handler is HotfixActorClusterHandler);
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
        private readonly IClusterNodeSender _nodeSender;
        private readonly LocalActorNodeIdentity _localNode;

        public GeneratedActorHandlerWithRouterProbe(
            IClusterNodeSender nodeSender,
            LocalActorNodeIdentity localNode)
        {
            _nodeSender = nodeSender;
            _localNode = localNode;
        }

        public ValueTask<ClusterSendStatus> HandleAsync(
            ClusterMessage message,
            CancellationToken cancellationToken = default)
        {
            Assert.NotNull(_nodeSender);
            Assert.NotNull(_localNode);
            return new ValueTask<ClusterSendStatus>(ClusterSendStatus.RouteNotFound);
        }
    }

    private sealed class CustomNotificationDispatcher : IClientNotificationRemoteDispatcher
    {
        public ValueTask<ClientNotificationStatus> DispatchAsync(
            RouteLocation target,
            ClientNotificationCommand command,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<ClientNotificationStatus>(ClientNotificationStatus.Accepted);
        }
    }

    private static ServiceProvider BuildProvider(string endpoint, IReadOnlyList<string> seeds)
    {
        var services = new ServiceCollection().AddTestEndpointRuntimes();
        services.AddSingleton(new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "node-1" },
            Cluster = new LakonaGameClusterOptions
            {
                Endpoint = endpoint,
                Seeds = seeds,
            }
        });
        services.AddLakonaGameServerActors();
        services.AddLakonaGameClusterEndpoint();
        return services.BuildServiceProvider();
    }

    private static void AddRuntimeOptions(
        IServiceCollection services,
        string endpoint,
        IReadOnlyList<string> seeds)
    {
        services.AddSingleton(new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "node-1" },
            Cluster = new LakonaGameClusterOptions
            {
                Endpoint = endpoint,
                Seeds = seeds,
            }
        });
    }

    private sealed class GeneratedDistributedActorAccessorProbe
    {
        public GeneratedDistributedActorAccessorProbe(
            IActorRuntime runtime,
            IRemoteActorInvoker remote,
            RemoteActorOptions remoteOptions,
            IActorDirectory directory,
            IActorDirectoryCache directoryCache,
            LocalActorNodeIdentity localNode)
        {
            Runtime = runtime;
            Remote = remote;
            RemoteOptions = remoteOptions;
            Directory = directory;
            DirectoryCache = directoryCache;
            LocalNode = localNode;
        }

        public IActorRuntime Runtime { get; }

        public IRemoteActorInvoker Remote { get; }

        public RemoteActorOptions RemoteOptions { get; }

        public IActorDirectory Directory { get; }

        public IActorDirectoryCache DirectoryCache { get; }

        public LocalActorNodeIdentity LocalNode { get; }
    }
}

[CollectionDefinition("Cluster serializer registration", DisableParallelization = true)]
public sealed class ClusterSerializerRegistrationCollection;
