using System;
using System.Threading;
using System.Threading.Tasks;
using Lakona.Game.Cluster;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server;

namespace Lakona.Game.Cluster.Rpc
{
    public sealed class ULinkRpcRouteDirectoryBinder
    {
        private readonly IRouteDirectory _directory;

        public ULinkRpcRouteDirectoryBinder(IRouteDirectory directory)
        {
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        }

        public void Bind(RpcServiceRegistry registry)
        {
            if (registry is null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            registry.Register(ULinkRpcClusterProtocol.ServiceId, ULinkRpcClusterProtocol.RegisterRouteMethodId, RegisterAsync);
            registry.Register(ULinkRpcClusterProtocol.ServiceId, ULinkRpcClusterProtocol.ResolveRouteMethodId, ResolveAsync);
            registry.Register(ULinkRpcClusterProtocol.ServiceId, ULinkRpcClusterProtocol.RefreshRouteLeaseMethodId, RefreshLeaseAsync);
            registry.Register(ULinkRpcClusterProtocol.ServiceId, ULinkRpcClusterProtocol.ExpireRoutesMethodId, ExpireAsync);
            registry.Register(ULinkRpcClusterProtocol.ServiceId, ULinkRpcClusterProtocol.ClearRoutesByNodeMethodId, ClearByNodeAsync);
            registry.Register(ULinkRpcClusterProtocol.ServiceId, ULinkRpcClusterProtocol.ClearRoutesByNodeEpochMethodId, ClearByNodeEpochAsync);
        }

        public static void Bind(RpcServiceRegistry registry, IRouteDirectory directory)
        {
            new ULinkRpcRouteDirectoryBinder(directory).Bind(registry);
        }

        private async ValueTask<TransportFrame> RegisterAsync(
            RpcSession session,
            RpcRequestFrame request,
            CancellationToken cancellationToken)
        {
            var dto = session.Serializer.Deserialize<ULinkRpcRouteRegisterRequest>(request.Payload.Memory);
            if (dto.Location is null)
            {
                throw new InvalidOperationException("Route location is required.");
            }

            var status = await _directory.RegisterAsync(
                ULinkRpcRouteLocationConverter.ToRouteLocation(dto.Location),
                cancellationToken).ConfigureAwait(false);

            return EncodeReply(session, request, new ULinkRpcRouteRegisterReply
            {
                Status = (int)status
            });
        }

        private async ValueTask<TransportFrame> ResolveAsync(
            RpcSession session,
            RpcRequestFrame request,
            CancellationToken cancellationToken)
        {
            var dto = session.Serializer.Deserialize<ULinkRpcRouteResolveRequest>(request.Payload.Memory);
            var location = await _directory.ResolveAsync(
                dto.Route,
                dto.Now,
                cancellationToken).ConfigureAwait(false);

            return EncodeReply(session, request, new ULinkRpcRouteResolveReply
            {
                Location = location is null ? null : ULinkRpcRouteLocationConverter.ToDto(location)
            });
        }

        private async ValueTask<TransportFrame> RefreshLeaseAsync(
            RpcSession session,
            RpcRequestFrame request,
            CancellationToken cancellationToken)
        {
            var dto = session.Serializer.Deserialize<ULinkRpcRouteRefreshLeaseRequest>(request.Payload.Memory);
            if (dto.ExpectedLocation is null)
            {
                throw new InvalidOperationException("Expected route location is required.");
            }

            var status = await _directory.RefreshLeaseAsync(
                ULinkRpcRouteLocationConverter.ToRouteLocation(dto.ExpectedLocation),
                dto.ExpiresAt,
                dto.Now,
                cancellationToken).ConfigureAwait(false);

            return EncodeReply(session, request, new ULinkRpcRouteRefreshLeaseReply
            {
                Status = (int)status
            });
        }

        private async ValueTask<TransportFrame> ExpireAsync(
            RpcSession session,
            RpcRequestFrame request,
            CancellationToken cancellationToken)
        {
            var dto = session.Serializer.Deserialize<ULinkRpcRouteExpireRequest>(request.Payload.Memory);
            var removed = await _directory.ExpireAsync(dto.Now, cancellationToken).ConfigureAwait(false);
            return EncodeReply(session, request, new ULinkRpcRouteExpireReply
            {
                Removed = removed
            });
        }

        private async ValueTask<TransportFrame> ClearByNodeAsync(
            RpcSession session,
            RpcRequestFrame request,
            CancellationToken cancellationToken)
        {
            var dto = session.Serializer.Deserialize<ULinkRpcRouteClearByNodeRequest>(request.Payload.Memory);
            var removed = await _directory.ClearByNodeAsync(dto.Node, cancellationToken).ConfigureAwait(false);
            return EncodeReply(session, request, new ULinkRpcRouteClearReply
            {
                Removed = removed
            });
        }

        private async ValueTask<TransportFrame> ClearByNodeEpochAsync(
            RpcSession session,
            RpcRequestFrame request,
            CancellationToken cancellationToken)
        {
            var dto = session.Serializer.Deserialize<ULinkRpcRouteClearByNodeEpochRequest>(request.Payload.Memory);
            var removed = await _directory.ClearByNodeEpochAsync(
                dto.Node,
                dto.NodeEpoch,
                cancellationToken).ConfigureAwait(false);

            return EncodeReply(session, request, new ULinkRpcRouteClearReply
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
