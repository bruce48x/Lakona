using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Actors;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Actors.Internal;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Sessions;
using Lakona.Game.Cluster.Rpc.Membership;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;

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

        services.TryAddSingleton<ClusterRpcChannel>(static _ => new ClusterRpcChannel());
        services.TryAddSingleton(new ActorHostDescriptorCatalog([]));
        services.TryAddSingleton(new StartupActorDescriptorCatalog([]));
        var runtimeOptions = FindRuntimeOptions(services) ?? new LakonaGameRuntimeOptions();
        if (!services.Any(static descriptor => descriptor.ServiceType == typeof(LakonaGameRuntimeOptions)))
        {
            services.AddSingleton(runtimeOptions);
        }

        services.TryAddSingleton<IClusterClientFactory>(provider => new ClusterClientFactory(
            provider.GetRequiredService<ClusterRpcChannel>(),
            loggerFactory: provider.GetService<ILoggerFactory>()));
        services.TryAddSingleton<IClusterMembershipTransport>(provider =>
        {
            var clusterOptions = provider.GetService<ClusterOptions>()
                ?? provider.GetRequiredService<LakonaGameRuntimeOptions>().ToClusterOptions();
            var requestTimeout = TimeSpan.FromMilliseconds(
                clusterOptions.SendTimeoutMilliseconds);
            var membershipOptions = provider.GetService<ClusterMembershipNodeOptions>()
                ?? new ClusterMembershipNodeOptions();
            membershipOptions.ValidateRequestTimeout(requestTimeout);
            return new RpcClusterMembershipTransport(
                provider.GetRequiredService<IClusterClientFactory>(),
                requestTimeout);
        });
        services.TryAddSingleton<ClusterCapabilityIndex>();
        services.TryAddSingleton<LocalClientNotificationCommandDispatcher>();
        services.TryAddSingleton(runtimeOptions.Notifications.ToBatchOptions());
        services.RemoveAll<IClientNotificationRemoteDispatcher>();
        services.AddSingleton<IClientNotificationRemoteDispatcher, ClusterClientNotificationDispatcher>();

        services.RemoveAll<IGameSessionIdFactory>();
        services.AddSingleton<IGameSessionIdFactory>(provider => new MembershipGameSessionIdFactory(
            provider.GetRequiredService<IClusterMembership>(), new NodeId(provider.GetRequiredService<LakonaGameRuntimeOptions>().Node.Id)));

        var hasActorRuntime = services.Any(
            static descriptor => descriptor.ServiceType == typeof(IActorRuntime));
        if (hasActorRuntime)
        {
            services.TryAddSingleton<ActorLifecycleRpcHandler>();
            services.RemoveAll<IStartupActorAffinityDirectory>();
            services.AddSingleton<StartupActorAffinityDirectory>();
            services.AddSingleton<IStartupActorAffinityDirectory>(provider =>
                provider.GetRequiredService<StartupActorAffinityDirectory>());
            services.TryAddSingleton<IActorDirectoryCache, InMemoryActorDirectoryCache>();
            services.RemoveAll<IActorDirectory>();
            services.RemoveAll<IActorActivationDirectory>();
            services.AddSingleton<ActorLocationDirectory>();
            services.AddSingleton<IActorLocationStabilizer>(provider =>
                provider.GetRequiredService<ActorLocationDirectory>());
            services.AddHostedService<ActorLocationCoordinator>();
            services.AddSingleton<IActorDirectory>(provider =>
                provider.GetRequiredService<ActorLocationDirectory>());
            services.AddSingleton<IActorActivationDirectory>(provider =>
                provider.GetRequiredService<ActorLocationDirectory>());
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IHostedService, ActorActivationPopulationDiagnostics>());
            services.RemoveAll<IActorPlacementService>();
            services.AddSingleton<IActorPlacementService>(provider => new ActorPlacementService(
                provider.GetRequiredService<IActorDirectory>(),
                provider.GetRequiredService<ClusterCapabilityIndex>(),
                provider.GetRequiredService<IActorHostClient>(),
                provider.GetRequiredService<ActorHosting>(),
                provider.GetRequiredService<LocalActorNodeIdentity>(),
                provider.GetRequiredService<IHotfixRuntimeAccessor>(),
                provider.GetRequiredService<IClusterMembership>(),
                provider.GetRequiredService<ActorCompensationLifetime>()));
        }
        services.TryAddSingleton<HotfixActorClusterHandler>();
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
