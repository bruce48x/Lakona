using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lakona.Game.Cluster;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server;

namespace Lakona.Game.Cluster.Rpc
{
    public sealed class ULinkRpcNodeDirectoryBinder
    {
        private readonly INodeDirectory _directory;

        public ULinkRpcNodeDirectoryBinder(INodeDirectory directory)
        {
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        }

        public void Bind(RpcServiceRegistry registry)
        {
            if (registry is null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            registry.Register(ULinkRpcClusterProtocol.ServiceId, ULinkRpcClusterProtocol.RegisterNodeMethodId, RegisterAsync);
            registry.Register(ULinkRpcClusterProtocol.ServiceId, ULinkRpcClusterProtocol.HeartbeatNodeMethodId, HeartbeatAsync);
            registry.Register(ULinkRpcClusterProtocol.ServiceId, ULinkRpcClusterProtocol.UpdateNodeStateMethodId, UpdateStateAsync);
            registry.Register(ULinkRpcClusterProtocol.ServiceId, ULinkRpcClusterProtocol.ResolveNodeMethodId, ResolveAsync);
            registry.Register(ULinkRpcClusterProtocol.ServiceId, ULinkRpcClusterProtocol.QueryNodesMethodId, QueryAsync);
            registry.Register(ULinkRpcClusterProtocol.ServiceId, ULinkRpcClusterProtocol.ExpireNodesMethodId, ExpireAsync);
        }

        public static void Bind(RpcServiceRegistry registry, INodeDirectory directory)
        {
            new ULinkRpcNodeDirectoryBinder(directory).Bind(registry);
        }

        private async ValueTask<TransportFrame> RegisterAsync(
            RpcSession session,
            RpcRequestFrame request,
            CancellationToken cancellationToken)
        {
            var dto = session.Serializer.Deserialize<ULinkRpcNodeRegisterRequest>(request.Payload.Memory);
            if (dto.Registration is null)
            {
                throw new InvalidOperationException("Node registration is required.");
            }

            var result = await _directory.RegisterAsync(
                ULinkRpcNodeDirectoryRecordConverter.ToNodeRegistration(dto.Registration),
                dto.Now,
                cancellationToken).ConfigureAwait(false);

            return EncodeReply(session, request, new ULinkRpcNodeRegisterReply
            {
                Status = (int)result.Status,
                Record = result.Record is null ? null : ULinkRpcNodeDirectoryRecordConverter.ToDto(result.Record)
            });
        }

        private async ValueTask<TransportFrame> HeartbeatAsync(
            RpcSession session,
            RpcRequestFrame request,
            CancellationToken cancellationToken)
        {
            var dto = session.Serializer.Deserialize<ULinkRpcNodeHeartbeatRequest>(request.Payload.Memory);
            var status = await _directory.HeartbeatAsync(
                dto.ClusterName,
                dto.Node,
                dto.NodeEpoch,
                dto.LeaseExpiresAt,
                dto.Now,
                cancellationToken).ConfigureAwait(false);

            return EncodeReply(session, request, new ULinkRpcNodeHeartbeatReply
            {
                Status = (int)status
            });
        }

        private async ValueTask<TransportFrame> UpdateStateAsync(
            RpcSession session,
            RpcRequestFrame request,
            CancellationToken cancellationToken)
        {
            var dto = session.Serializer.Deserialize<ULinkRpcNodeUpdateStateRequest>(request.Payload.Memory);
            var status = await _directory.UpdateStateAsync(
                dto.ClusterName,
                dto.Node,
                dto.NodeEpoch,
                ToNodeState(dto.State),
                dto.Now,
                cancellationToken).ConfigureAwait(false);

            return EncodeReply(session, request, new ULinkRpcNodeUpdateStateReply
            {
                Status = (int)status
            });
        }

        private async ValueTask<TransportFrame> ResolveAsync(
            RpcSession session,
            RpcRequestFrame request,
            CancellationToken cancellationToken)
        {
            var dto = session.Serializer.Deserialize<ULinkRpcNodeResolveRequest>(request.Payload.Memory);
            var record = await _directory.ResolveAsync(
                dto.ClusterName,
                dto.Node,
                dto.Now,
                cancellationToken).ConfigureAwait(false);

            return EncodeReply(session, request, new ULinkRpcNodeResolveReply
            {
                Record = record is null ? null : ULinkRpcNodeDirectoryRecordConverter.ToDto(record)
            });
        }

        private async ValueTask<TransportFrame> QueryAsync(
            RpcSession session,
            RpcRequestFrame request,
            CancellationToken cancellationToken)
        {
            var dto = session.Serializer.Deserialize<ULinkRpcNodeQueryRequest>(request.Payload.Memory);
            if (dto.Query is null)
            {
                throw new InvalidOperationException("Node directory query is required.");
            }

            var records = await _directory.QueryAsync(
                ULinkRpcNodeDirectoryRecordConverter.ToNodeDirectoryQuery(dto.Query),
                dto.Now,
                cancellationToken).ConfigureAwait(false);

            var recordDtos = new List<ULinkRpcNodeRecordDto>(records.Count);
            for (var i = 0; i < records.Count; i++)
            {
                recordDtos.Add(ULinkRpcNodeDirectoryRecordConverter.ToDto(records[i]));
            }

            return EncodeReply(session, request, new ULinkRpcNodeQueryReply
            {
                Records = recordDtos
            });
        }

        private async ValueTask<TransportFrame> ExpireAsync(
            RpcSession session,
            RpcRequestFrame request,
            CancellationToken cancellationToken)
        {
            var dto = session.Serializer.Deserialize<ULinkRpcNodeExpireRequest>(request.Payload.Memory);
            var expired = await _directory.ExpireAsync(
                dto.ClusterName,
                dto.Now,
                cancellationToken).ConfigureAwait(false);

            return EncodeReply(session, request, new ULinkRpcNodeExpireReply
            {
                Expired = expired
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

        private static NodeState ToNodeState(int value)
        {
            if (!Enum.IsDefined(typeof(NodeState), value))
            {
                throw new InvalidOperationException("Node state value is invalid.");
            }

            return (NodeState)value;
        }
    }
}
