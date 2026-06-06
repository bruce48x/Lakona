using System;
using System.Threading;
using System.Threading.Tasks;

namespace Lakona.Game.Cluster
{
    public interface IRouteDirectory
    {
        ValueTask<RouteRegistrationStatus> RegisterAsync(
            RouteLocation location,
            CancellationToken cancellationToken = default);

        ValueTask<RouteLocation?> ResolveAsync(
            RouteKey route,
            DateTimeOffset now,
            CancellationToken cancellationToken = default);

        ValueTask<RouteLeaseRefreshStatus> RefreshLeaseAsync(
            RouteLocation expectedLocation,
            DateTimeOffset expiresAt,
            DateTimeOffset now,
            CancellationToken cancellationToken = default);

        ValueTask<int> ExpireAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken = default);

        ValueTask<int> ClearByNodeAsync(
            NodeId node,
            CancellationToken cancellationToken = default);

        ValueTask<int> ClearByNodeEpochAsync(
            NodeId node,
            long nodeEpoch,
            CancellationToken cancellationToken = default);
    }
}
