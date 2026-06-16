using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Configuration;
using Lakona.Rpc.Server;

namespace Lakona.Game.Server.Sessions;

public static class SessionServiceCollectionExtensions
{
    public static IServiceCollection AddLakonaGameServerSessions(this IServiceCollection services)
    {
        services.TryAddSingleton<IGameSessionDirectory, InMemoryGameSessionDirectory>();
        services.TryAddSingleton<IGameSessionResumeService, GameSessionResumeService>();
        services.TryAddSingleton<IGameSessionConnectionCloser, NoopGameSessionConnectionCloser>();
        services.TryAddSingleton<IClientNotificationRemoteDispatcher, NoopClientNotificationRemoteDispatcher>();
        services.TryAddSingleton<IClientNotificationRelay>(CreateClientNotificationRelay);
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

        services.TryAddSingleton(options);
        services.AddHostedService<GameSessionCleanupHostedService>();
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
        var sessions = services.GetRequiredService<IGameSessionDirectory>();
        var routes = services.GetService<IRouteDirectory>();
        var dispatcher = services.GetService<IClientNotificationRemoteDispatcher>();
        var cluster = services.GetService<ClusterOptions>();
        NodeId? localNode = cluster is null ? (NodeId?)null : new NodeId(cluster.NodeId);
        return new ClientNotificationRelay(sessions, routes, dispatcher, localNode);
    }
}
