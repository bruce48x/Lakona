using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.ReliablePush;
using Lakona.Rpc.Server;

namespace Lakona.Game.Server.Sessions;

public static class SessionServiceCollectionExtensions
{
    public static IServiceCollection AddLakonaGameServerSessions(this IServiceCollection services)
    {
        services.TryAddSingleton<IGameSessionRegistry, InMemoryGameSessionRegistry>();
        services.TryAddSingleton<IGameSessionResumeService, GameSessionResumeService>();
        services.TryAddSingleton<IGameHeartbeatService, GameHeartbeatService>();
        services.TryAddSingleton<IGameSessionConnectionCloser, NoopGameSessionConnectionCloser>();
        services.TryAddSingleton<IClientNotifications, ClientNotifications>();
        services.TryAddSingleton<IClientNotificationRelay>(CreateClientNotificationRelay);
        services.TryAddSingleton<IClientNotificationRemoteDispatcher, NoopClientNotificationRemoteDispatcher>();
        services.TryAddSingleton<LocalClientNotificationCommandDispatcher>();
        services.TryAddSingleton<IClientNotificationCommandRouter>(CreateClientNotificationCommandRouter);
        services.TryAddSingleton<IClientSessionRouteRegistrar>(CreateClientSessionRouteRegistrar);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRpcSessionLifecycleObserver, GameSessionRpcLifecycleObserver>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IGameSessionLifecycleHandler, ClientSessionRouteLifecycleHandler>());
        return services;
    }

    public static IServiceCollection AddLakonaGameServerSessionCleanup(
        this IServiceCollection services,
        Action<SessionCleanupOptions>? configure = null)
    {
        services.AddLakonaGameServerSessions();

        var options = new SessionCleanupOptions();
        configure?.Invoke(options);

        services.RemoveAll<SessionCleanupOptions>();
        services.AddSingleton(options);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, GameSessionCleanupHostedService>());
        return services;
    }

    public static IServiceCollection AddLakonaGameServerSessionCleanup(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<SessionCleanupOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var hosting = LakonaGameHostingOptions.FromConfiguration(configuration);
        services.AddLakonaGameServerSessions();

        var options = new SessionCleanupOptions();
        hosting.Sessions.Cleanup.ApplyTo(options);
        configure?.Invoke(options);

        services.RemoveAll<SessionCleanupOptions>();
        services.AddSingleton(options);
        if (hosting.Sessions.Cleanup.Enabled)
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, GameSessionCleanupHostedService>());
        }

        return services;
    }

    private static IClientSessionRouteRegistrar CreateClientSessionRouteRegistrar(IServiceProvider services)
    {
        var routes = services.GetService<IRouteDirectory>();
        var cluster = services.GetService<ClusterOptions>();
        if (routes is null ||
            cluster is null ||
            !cluster.AdvertisedEndpoints.TryGetValue("cluster", out var clusterEndpoint) ||
            string.IsNullOrWhiteSpace(clusterEndpoint))
        {
            return new NoopClientSessionRouteRegistrar();
        }

        return new ClientSessionRouteRegistrar(
            routes,
            new NodeId(cluster.NodeId),
            new NodeEndpoint(clusterEndpoint),
            TimeSpan.FromSeconds(cluster.RouteLeaseSeconds));
    }

    private static IClientNotificationRelay CreateClientNotificationRelay(IServiceProvider services)
    {
        var sessions = services.GetRequiredService<IGameSessionRegistry>();
        var routes = services.GetService<IRouteDirectory>();
        var dispatcher = services.GetService<IClientNotificationRemoteDispatcher>();
        var cluster = services.GetService<ClusterOptions>();
        NodeId? localNode = cluster is null ? (NodeId?)null : new NodeId(cluster.NodeId);
        return new ClientNotificationRelay(sessions, routes, dispatcher, localNode);
    }

    private static IClientNotificationCommandRouter CreateClientNotificationCommandRouter(IServiceProvider services)
    {
        var localOwner = services.GetRequiredService<IReliablePushRuntime>();
        var routes = services.GetService<IRouteDirectory>();
        var remoteDispatcher = services.GetService<IClientNotificationRemoteDispatcher>();
        var cluster = services.GetService<ClusterOptions>();
        NodeId? localNode = cluster is null ? (NodeId?)null : new NodeId(cluster.NodeId);
        return new ClientNotificationCommandRouter(localOwner, routes, remoteDispatcher, localNode);
    }
}
