using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Sessions;

public sealed class ClientSessionRouteRegistrar : IClientSessionRouteRegistrar
{
    private static readonly TimeSpan DefaultRouteLease = TimeSpan.FromMinutes(5);

    private readonly IRouteDirectory _routes;
    private readonly NodeId _gatewayNode;
    private readonly NodeEndpoint _clusterEndpoint;
    private readonly TimeSpan _routeLease;
    private readonly Func<DateTimeOffset> _utcNow;

    public ClientSessionRouteRegistrar(
        IRouteDirectory routes,
        NodeId gatewayNode,
        NodeEndpoint clusterEndpoint,
        TimeSpan? routeLease = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        _routes = routes ?? throw new ArgumentNullException(nameof(routes));
        _gatewayNode = gatewayNode;
        _clusterEndpoint = clusterEndpoint ?? throw new ArgumentNullException(nameof(clusterEndpoint));
        _routeLease = routeLease ?? DefaultRouteLease;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public async ValueTask RegisterAsync(
        GameSessionKey session,
        CancellationToken cancellationToken = default)
    {
        var route = ClientNotificationRouteKey.FromSession(session);
        var now = _utcNow();
        var status = await _routes.RegisterAsync(
            new RouteLocation(
                route,
                _gatewayNode,
                _clusterEndpoint,
                now.Add(_routeLease),
                generation: session.Generation),
            cancellationToken).ConfigureAwait(false);
        if (status != RouteRegistrationStatus.Registered)
        {
            throw new InvalidOperationException(
                $"Client session route '{route}' registration failed with status '{status}'.");
        }
    }

    public async ValueTask RemoveAsync(
        GameSessionKey session,
        CancellationToken cancellationToken = default)
    {
        await _routes.UnregisterAsync(
            ClientNotificationRouteKey.FromSession(session),
            cancellationToken).ConfigureAwait(false);
    }
}
