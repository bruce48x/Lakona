using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Sessions;
using Lakona.Game.Cluster.Rpc.Membership;

namespace Lakona.Game.Server.Hosting;

public static class LakonaClusterEndpointServiceCollectionExtensions
{
    public static IServiceCollection AddLakonaGameClusterEndpoint(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (!services.Any(static descriptor => descriptor.ServiceType == typeof(IClusterMembership)))
        {
            throw new InvalidOperationException(
                "A Lakona cluster endpoint requires IClusterMembership. Use AddLakonaGameServer for a clustered host or AddLakonaGameServerActors for a process-local actor runtime.");
        }
        if (!services.Any(static descriptor => descriptor.ServiceType == typeof(IClusterMembership)))
        {
            throw new InvalidOperationException(
                "A Lakona cluster endpoint requires IClusterMembership. Use AddLakonaGameServer for a clustered host or AddLakonaGameServerActors for a process-local actor runtime.");
        }

        services.TryAddSingleton<ClusterRpcChannel>(static _ => new ClusterRpcChannel());
        services.TryAddSingleton(new ActorHostDescriptorCatalog([]));
        services.TryAddSingleton(new StartupActorDescriptorCatalog([]));
        var runtimeOptions = FindRuntimeOptions(services) ?? new LakonaGameRuntimeOptions();
        if (!services.Any(static descriptor => descriptor.ServiceType == typeof(LakonaGameRuntimeOptions)))
        {
            services.AddSingleton(runtimeOptions);
        }

        services.TryAddSingleton<IClusterClientFactory>(provider => new ClusterClientFactory(
            provider.GetRequiredService<ClusterRpcChannel>()));
        services.TryAddSingleton<IClusterMembershipTransport, RpcClusterMembershipTransport>();
        services.TryAddSingleton<ClusterLocalMessageHandler>();
        services.TryAddSingleton<INodeMessenger, ClusterNodeMessenger>();
        services.TryAddSingleton(new ClusterNodeSenderOptions());
        services.TryAddSingleton<ClusterCapabilityIndex>();
        services.TryAddSingleton<IClusterNodeSender>(provider => new ClusterNodeSender(
            provider.GetRequiredService<IClusterMembership>(),
            provider.GetRequiredService<INodeMessenger>()));
        services.TryAddSingleton<IExactClusterNodeSender>(provider =>
            (IExactClusterNodeSender)provider.GetRequiredService<IClusterNodeSender>());
        services.TryAddSingleton<LocalClientNotificationCommandDispatcher>();
        services.TryAddSingleton(runtimeOptions.Notifications.ToBatchOptions());
        RemoveSessionOnlyNotificationDispatcher(services);
        services.TryAddSingleton<IClientNotificationRemoteDispatcher, ClusterClientNotificationDispatcher>();

        services.RemoveAll<IRouteDirectory>();
        services.TryAddSingleton<InMemoryRouteDirectory>();
        services.AddSingleton<IRouteDirectory>(provider => new MembershipSessionRouteDirectory(
            provider.GetRequiredService<InMemoryRouteDirectory>(), provider.GetRequiredService<IClusterMembership>()));
        services.RemoveAll<IGameSessionIdFactory>();
        services.AddSingleton<IGameSessionIdFactory>(provider => new MembershipGameSessionIdFactory(
            provider.GetRequiredService<IClusterMembership>(), new NodeId(provider.GetRequiredService<LakonaGameRuntimeOptions>().Node.Id)));

        var hasActorRuntime = services.Any(
            static descriptor => descriptor.ServiceType == typeof(IActorRuntime));
        RemoveActorDirectoryHandlerDescriptors(services);
        if (hasActorRuntime)
        {
            services.RemoveAll<IActorDirectory>();
            services.AddSingleton<ReplicatedActorActivationDirectory>();
            services.AddSingleton<IActorDirectory>(provider =>
                provider.GetRequiredService<ReplicatedActorActivationDirectory>());
            services.TryAddSingleton<IActorActivationDirectory>(provider =>
                provider.GetRequiredService<ReplicatedActorActivationDirectory>());
            services.AddSingleton<IClusterMessageHandler>(ResolveReplicatedActorActivationDirectory);
            services.RemoveAll<IActorPlacementService>();
            services.AddSingleton<IActorPlacementService>(provider => new ActorPlacementService(
                provider.GetRequiredService<IActorDirectory>(),
                provider.GetRequiredService<ClusterCapabilityIndex>(),
                provider.GetRequiredService<IActorHostClient>(),
                provider.GetRequiredService<ActorHosting>(),
                provider.GetRequiredService<LocalActorNodeIdentity>(),
                provider.GetRequiredService<IHotfixRuntimeAccessor>(),
                provider.GetRequiredService<IClusterMembership>()));
        }
        services.TryAddSingleton<IClusterRouter>(provider => new ClusterRouter(
            new NodeId(provider
                .GetRequiredService<LakonaGameRuntimeOptions>()
                .Node.Id),
            provider.GetRequiredService<IRouteDirectory>(),
            provider.GetRequiredService<ClusterLocalMessageHandler>(),
            provider.GetRequiredService<INodeMessenger>()));
        services.TryAddSingleton<HotfixActorClusterHandler>();
        if (!services.Any(static descriptor =>
                descriptor.ServiceType == typeof(IClusterMessageHandler)
                && descriptor.ImplementationFactory?.Method
                    == ((Func<IServiceProvider, IClusterMessageHandler>)ResolveHotfixActorClusterHandler)
                        .Method))
        {
            services.AddSingleton<IClusterMessageHandler>(ResolveHotfixActorClusterHandler);
        }
        services.TryAddSingleton<IClusterActorTransport>(provider => new RpcClusterActorTransport(
            provider.GetRequiredService<IClusterClientFactory>(),
            provider.GetRequiredService<IClusterMembership>()));
        services.TryAddSingleton<IRemoteActorInvoker>(provider => new RemoteActorInvoker(
            provider.GetRequiredService<IClusterActorTransport>(),
            provider.GetService<IActorDirectory>(),
            provider.GetService<IActorDirectoryCache>()));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRpcServerConfigurator>(
            new LakonaClusterRpcServerConfigurator(runtimeOptions)));
        return services;
    }

    private static void RemoveActorDirectoryHandlerDescriptors(IServiceCollection services)
    {
        for (var index = services.Count - 1; index >= 0; index--)
        {
            var descriptor = services[index];
            if (descriptor.ServiceType == typeof(IClusterMessageHandler) &&
                (descriptor.ImplementationType == typeof(ActorDirectoryClusterHandler)
                    || descriptor.ImplementationFactory?.Method
                        == ((Func<IServiceProvider, IClusterMessageHandler>)ResolveReplicatedActorActivationDirectory)
                            .Method))
            {
                services.RemoveAt(index);
            }
        }
    }

    private static IClusterMessageHandler ResolveReplicatedActorActivationDirectory(
        IServiceProvider provider) =>
        provider.GetRequiredService<ReplicatedActorActivationDirectory>();

    private static IClusterMessageHandler ResolveHotfixActorClusterHandler(
        IServiceProvider provider) =>
        provider.GetRequiredService<HotfixActorClusterHandler>();

    private static void RemoveSessionOnlyNotificationDispatcher(IServiceCollection services)
    {
        for (var index = services.Count - 1; index >= 0; index--)
        {
            var descriptor = services[index];
            if (descriptor.ServiceType == typeof(IClientNotificationRemoteDispatcher) &&
                descriptor.ImplementationType == typeof(NoopClientNotificationRemoteDispatcher))
            {
                services.RemoveAt(index);
            }
        }
    }

    private static LakonaGameRuntimeOptions? FindRuntimeOptions(IServiceCollection services)
    {
        for (var i = services.Count - 1; i >= 0; i--)
        {
            var descriptor = services[i];
            if (descriptor.ServiceType == typeof(LakonaGameRuntimeOptions) &&
                descriptor.ImplementationInstance is LakonaGameRuntimeOptions options)
            {
                return options;
            }
        }

        return null;
    }
}
