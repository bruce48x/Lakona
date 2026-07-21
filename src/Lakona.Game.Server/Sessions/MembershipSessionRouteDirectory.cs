using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Sessions;

public sealed class MembershipSessionRouteDirectory : IRouteDirectory
{
    private const string Prefix = "client-session:";
    private readonly IRouteDirectory inner;
    private readonly IClusterMembership membership;

    public MembershipSessionRouteDirectory(
        IRouteDirectory inner,
        IClusterMembership membership)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.membership = membership ?? throw new ArgumentNullException(nameof(membership));
    }

    public ValueTask<RouteRegistrationStatus> RegisterAsync(
        RouteLocation location,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return TryDecode(location.Route, out var gateway) && gateway!.Node == location.Node
            ? ValueTask.FromResult(RouteRegistrationStatus.Registered)
            : inner.RegisterAsync(location, cancellationToken);
    }

    public ValueTask<RouteUnregisterStatus> UnregisterAsync(
        RouteKey route,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return TryDecode(route, out _)
            ? ValueTask.FromResult(RouteUnregisterStatus.Removed)
            : inner.UnregisterAsync(route, cancellationToken);
    }

    public ValueTask<RouteLocation?> ResolveAsync(
        RouteKey route,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryDecode(route, out var gateway))
        {
            return inner.ResolveAsync(route, now, cancellationToken);
        }

        var snapshot = membership.Current;
        if (!snapshot.TryGetMember(gateway!, out var member)
            || member is null
            || member.State != ClusterMemberState.Ready)
        {
            return ValueTask.FromResult<RouteLocation?>(null);
        }

        return ValueTask.FromResult<RouteLocation?>(new RouteLocation(
            route,
            gateway!,
            snapshot.View,
            member.ClusterEndpoint));
    }

    public ValueTask<RouteLeaseRefreshStatus> RefreshLeaseAsync(
        RouteLocation expectedLocation,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return TryDecode(expectedLocation.Route, out var gateway)
            && gateway!.Node == expectedLocation.Node
            ? ValueTask.FromResult(RouteLeaseRefreshStatus.Refreshed)
            : inner.RefreshLeaseAsync(expectedLocation, expiresAt, now, cancellationToken);
    }

    public ValueTask<int> ExpireAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        inner.ExpireAsync(now, cancellationToken);

    public ValueTask<int> ClearByNodeAsync(
        NodeId node,
        CancellationToken cancellationToken = default) =>
        inner.ClearByNodeAsync(node, cancellationToken);

    public ValueTask<int> ClearByNodeEpochAsync(
        NodeId node,
        long nodeEpoch,
        CancellationToken cancellationToken = default) =>
        inner.ClearByNodeEpochAsync(node, nodeEpoch, cancellationToken);

    private static bool TryDecode(RouteKey route, out NodeReference? gateway)
    {
        gateway = null;
        if (!route.Value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var slash = route.Value.LastIndexOf('/');
        return slash >= Prefix.Length
            && slash < route.Value.Length - 1
            && MembershipSessionLocator.TryDecode(route.Value[(slash + 1)..], out gateway);
    }
}
