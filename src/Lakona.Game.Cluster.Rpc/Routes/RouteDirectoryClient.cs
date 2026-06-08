using System;
using System.Threading;
using System.Threading.Tasks;
using Lakona.Game.Cluster;
using Lakona.Rpc.Core;

namespace Lakona.Game.Cluster.Rpc
{
    public sealed class RouteDirectoryClient : IRouteDirectory
    {
        private readonly IRpcClient _client;

        public RouteDirectoryClient(IRpcClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public async ValueTask<RouteRegistrationStatus> RegisterAsync(
            RouteLocation location,
            CancellationToken cancellationToken = default)
        {
            if (location is null)
            {
                throw new ArgumentNullException(nameof(location));
            }

            var reply = await _client.CallAsync(
                ClusterProtocol.RegisterRouteMethod,
                new RouteRegisterRequest
                {
                    Location = RouteLocationConverter.ToDto(location)
                },
                cancellationToken).ConfigureAwait(false);

            return Enum.IsDefined(typeof(RouteRegistrationStatus), reply.Status)
                ? (RouteRegistrationStatus)reply.Status
                : RouteRegistrationStatus.StaleLocation;
        }

        public async ValueTask<RouteLocation?> ResolveAsync(
            RouteKey route,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            var reply = await _client.CallAsync(
                ClusterProtocol.ResolveRouteMethod,
                new RouteResolveRequest
                {
                    Route = route.Value,
                    Now = now
                },
                cancellationToken).ConfigureAwait(false);

            return reply.Location is null
                ? null
                : RouteLocationConverter.ToRouteLocation(reply.Location);
        }

        public async ValueTask<RouteLeaseRefreshStatus> RefreshLeaseAsync(
            RouteLocation expectedLocation,
            DateTimeOffset expiresAt,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            if (expectedLocation is null)
            {
                throw new ArgumentNullException(nameof(expectedLocation));
            }

            var reply = await _client.CallAsync(
                ClusterProtocol.RefreshRouteLeaseMethod,
                new RouteRefreshLeaseRequest
                {
                    ExpectedLocation = RouteLocationConverter.ToDto(expectedLocation),
                    ExpiresAt = expiresAt,
                    Now = now
                },
                cancellationToken).ConfigureAwait(false);

            return Enum.IsDefined(typeof(RouteLeaseRefreshStatus), reply.Status)
                ? (RouteLeaseRefreshStatus)reply.Status
                : RouteLeaseRefreshStatus.StaleLocation;
        }

        public async ValueTask<int> ExpireAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            var reply = await _client.CallAsync(
                ClusterProtocol.ExpireRoutesMethod,
                new RouteExpireRequest
                {
                    Now = now
                },
                cancellationToken).ConfigureAwait(false);

            return reply.Removed;
        }

        public async ValueTask<int> ClearByNodeAsync(
            NodeId node,
            CancellationToken cancellationToken = default)
        {
            var reply = await _client.CallAsync(
                ClusterProtocol.ClearRoutesByNodeMethod,
                new RouteClearByNodeRequest
                {
                    Node = node.Value
                },
                cancellationToken).ConfigureAwait(false);

            return reply.Removed;
        }

        public async ValueTask<int> ClearByNodeEpochAsync(
            NodeId node,
            long nodeEpoch,
            CancellationToken cancellationToken = default)
        {
            if (nodeEpoch < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(nodeEpoch), "Node epoch cannot be negative.");
            }

            var reply = await _client.CallAsync(
                ClusterProtocol.ClearRoutesByNodeEpochMethod,
                new RouteClearByNodeEpochRequest
                {
                    Node = node.Value,
                    NodeEpoch = nodeEpoch
                },
                cancellationToken).ConfigureAwait(false);

            return reply.Removed;
        }
    }
}
