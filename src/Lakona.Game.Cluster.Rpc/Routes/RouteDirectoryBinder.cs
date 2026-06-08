using System;
using System.Threading;
using System.Threading.Tasks;
using Lakona.Game.Cluster;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server;

namespace Lakona.Game.Cluster.Rpc
{
    public sealed class RouteDirectoryBinder
    {
        private readonly IRouteDirectory _directory;

        public RouteDirectoryBinder(IRouteDirectory directory)
        {
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        }

        public void Bind(RpcServiceRegistry registry)
        {
            if (registry is null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            registry.Register(ClusterProtocol.ServiceId, ClusterProtocol.RegisterRouteMethodId, RegisterAsync);
            registry.Register(ClusterProtocol.ServiceId, ClusterProtocol.ResolveRouteMethodId, ResolveAsync);
            registry.Register(ClusterProtocol.ServiceId, ClusterProtocol.RefreshRouteLeaseMethodId, RefreshLeaseAsync);
            registry.Register(ClusterProtocol.ServiceId, ClusterProtocol.ExpireRoutesMethodId, ExpireAsync);
            registry.Register(ClusterProtocol.ServiceId, ClusterProtocol.ClearRoutesByNodeMethodId, ClearByNodeAsync);
            registry.Register(ClusterProtocol.ServiceId, ClusterProtocol.ClearRoutesByNodeEpochMethodId, ClearByNodeEpochAsync);
        }

        public static void Bind(RpcServiceRegistry registry, IRouteDirectory directory)
        {
            new RouteDirectoryBinder(directory).Bind(registry);
        }

        private async ValueTask<TransportFrame> RegisterAsync(
            RpcSession session,
            RpcRequestFrame request,
            CancellationToken cancellationToken)
        {
            var dto = session.Serializer.Deserialize<RouteRegisterRequest>(request.Payload.Memory);
            if (dto.Location is null)
            {
                throw new InvalidOperationException("Route location is required.");
            }

            var status = await _directory.RegisterAsync(
                RouteLocationConverter.ToRouteLocation(dto.Location),
                cancellationToken).ConfigureAwait(false);

            return EncodeReply(session, request, new RouteRegisterReply
            {
                Status = (int)status
            });
        }

        private async ValueTask<TransportFrame> ResolveAsync(
            RpcSession session,
            RpcRequestFrame request,
            CancellationToken cancellationToken)
        {
            var dto = session.Serializer.Deserialize<RouteResolveRequest>(request.Payload.Memory);
            var location = await _directory.ResolveAsync(
                dto.Route,
                dto.Now,
                cancellationToken).ConfigureAwait(false);

            return EncodeReply(session, request, new RouteResolveReply
            {
                Location = location is null ? null : RouteLocationConverter.ToDto(location)
            });
        }

        private async ValueTask<TransportFrame> RefreshLeaseAsync(
            RpcSession session,
            RpcRequestFrame request,
            CancellationToken cancellationToken)
        {
            var dto = session.Serializer.Deserialize<RouteRefreshLeaseRequest>(request.Payload.Memory);
            if (dto.ExpectedLocation is null)
            {
                throw new InvalidOperationException("Expected route location is required.");
            }

            var status = await _directory.RefreshLeaseAsync(
                RouteLocationConverter.ToRouteLocation(dto.ExpectedLocation),
                dto.ExpiresAt,
                dto.Now,
                cancellationToken).ConfigureAwait(false);

            return EncodeReply(session, request, new RouteRefreshLeaseReply
            {
                Status = (int)status
            });
        }

        private async ValueTask<TransportFrame> ExpireAsync(
            RpcSession session,
            RpcRequestFrame request,
            CancellationToken cancellationToken)
        {
            var dto = session.Serializer.Deserialize<RouteExpireRequest>(request.Payload.Memory);
            var removed = await _directory.ExpireAsync(dto.Now, cancellationToken).ConfigureAwait(false);
            return EncodeReply(session, request, new RouteExpireReply
            {
                Removed = removed
            });
        }

        private async ValueTask<TransportFrame> ClearByNodeAsync(
            RpcSession session,
            RpcRequestFrame request,
            CancellationToken cancellationToken)
        {
            var dto = session.Serializer.Deserialize<RouteClearByNodeRequest>(request.Payload.Memory);
            var removed = await _directory.ClearByNodeAsync(dto.Node, cancellationToken).ConfigureAwait(false);
            return EncodeReply(session, request, new RouteClearReply
            {
                Removed = removed
            });
        }

        private async ValueTask<TransportFrame> ClearByNodeEpochAsync(
            RpcSession session,
            RpcRequestFrame request,
            CancellationToken cancellationToken)
        {
            var dto = session.Serializer.Deserialize<RouteClearByNodeEpochRequest>(request.Payload.Memory);
            var removed = await _directory.ClearByNodeEpochAsync(
                dto.Node,
                dto.NodeEpoch,
                cancellationToken).ConfigureAwait(false);

            return EncodeReply(session, request, new RouteClearReply
            {
                Removed = removed
            });
        }

        private static TransportFrame EncodeReply<T>(
            RpcSession session,
            RpcRequestFrame request,
            T reply)
        {
            using var payload = session.Serializer.SerializeFrame(reply);
            return RpcEnvelopeCodec.EncodeResponse(request.RequestId, RpcStatus.Ok, payload.Memory);
        }
    }
}
