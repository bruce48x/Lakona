using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lakona.Game.Cluster;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server;

namespace Lakona.Game.Cluster.Rpc
{
    public sealed class NodeDirectoryBinder
    {
        private readonly INodeDirectory _directory;

        public NodeDirectoryBinder(INodeDirectory directory)
        {
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        }

        public void Bind(RpcServiceRegistry registry)
        {
            if (registry is null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            registry.Register(ClusterProtocol.ServiceId, ClusterProtocol.RegisterNodeMethodId, RegisterAsync);
            registry.Register(ClusterProtocol.ServiceId, ClusterProtocol.HeartbeatNodeMethodId, HeartbeatAsync);
            registry.Register(ClusterProtocol.ServiceId, ClusterProtocol.UpdateNodeStateMethodId, UpdateStateAsync);
            registry.Register(ClusterProtocol.ServiceId, ClusterProtocol.ResolveNodeMethodId, ResolveAsync);
            registry.Register(ClusterProtocol.ServiceId, ClusterProtocol.QueryNodesMethodId, QueryAsync);
            registry.Register(ClusterProtocol.ServiceId, ClusterProtocol.ExpireNodesMethodId, ExpireAsync);
        }

        public static void Bind(RpcServiceRegistry registry, INodeDirectory directory)
        {
            new NodeDirectoryBinder(directory).Bind(registry);
        }

        private async ValueTask<TransportFrame> RegisterAsync(
            RpcSession session,
            RpcRequestFrame request,
            CancellationToken cancellationToken)
        {
            var dto = session.Serializer.Deserialize<NodeRegisterRequest>(request.Payload.Memory);
            if (dto.Registration is null)
            {
                throw new InvalidOperationException("Node registration is required.");
            }

            var result = await _directory.RegisterAsync(
                NodeDirectoryRecordConverter.ToNodeRegistration(dto.Registration),
                dto.Now,
                cancellationToken).ConfigureAwait(false);

            return EncodeReply(session, request, new NodeRegisterReply
            {
                Status = (int)result.Status,
                Record = result.Record is null ? null : NodeDirectoryRecordConverter.ToDto(result.Record)
            });
        }

        private async ValueTask<TransportFrame> HeartbeatAsync(
            RpcSession session,
            RpcRequestFrame request,
            CancellationToken cancellationToken)
        {
            var dto = session.Serializer.Deserialize<NodeHeartbeatRequest>(request.Payload.Memory);
            var status = await _directory.HeartbeatAsync(
                dto.ClusterName,
                dto.Node,
                dto.NodeEpoch,
                dto.LeaseExpiresAt,
                dto.Now,
                cancellationToken).ConfigureAwait(false);

            return EncodeReply(session, request, new NodeHeartbeatReply
            {
                Status = (int)status
            });
        }

        private async ValueTask<TransportFrame> UpdateStateAsync(
            RpcSession session,
            RpcRequestFrame request,
            CancellationToken cancellationToken)
        {
            var dto = session.Serializer.Deserialize<NodeUpdateStateRequest>(request.Payload.Memory);
            var status = await _directory.UpdateStateAsync(
                dto.ClusterName,
                dto.Node,
                dto.NodeEpoch,
                ToNodeState(dto.State),
                dto.Now,
                cancellationToken).ConfigureAwait(false);

            return EncodeReply(session, request, new NodeUpdateStateReply
            {
                Status = (int)status
            });
        }

        private async ValueTask<TransportFrame> ResolveAsync(
            RpcSession session,
            RpcRequestFrame request,
            CancellationToken cancellationToken)
        {
            var dto = session.Serializer.Deserialize<NodeResolveRequest>(request.Payload.Memory);
            var record = await _directory.ResolveAsync(
                dto.ClusterName,
                dto.Node,
                dto.Now,
                cancellationToken).ConfigureAwait(false);

            return EncodeReply(session, request, new NodeResolveReply
            {
                Record = record is null ? null : NodeDirectoryRecordConverter.ToDto(record)
            });
        }

        private async ValueTask<TransportFrame> QueryAsync(
            RpcSession session,
            RpcRequestFrame request,
            CancellationToken cancellationToken)
        {
            var dto = session.Serializer.Deserialize<NodeQueryRequest>(request.Payload.Memory);
            if (dto.Query is null)
            {
                throw new InvalidOperationException("Node directory query is required.");
            }

            var records = await _directory.QueryAsync(
                NodeDirectoryRecordConverter.ToNodeDirectoryQuery(dto.Query),
                dto.Now,
                cancellationToken).ConfigureAwait(false);

            var recordDtos = new List<NodeRecordDto>(records.Count);
            for (var i = 0; i < records.Count; i++)
            {
                recordDtos.Add(NodeDirectoryRecordConverter.ToDto(records[i]));
            }

            return EncodeReply(session, request, new NodeQueryReply
            {
                Records = recordDtos
            });
        }

        private async ValueTask<TransportFrame> ExpireAsync(
            RpcSession session,
            RpcRequestFrame request,
            CancellationToken cancellationToken)
        {
            var dto = session.Serializer.Deserialize<NodeExpireRequest>(request.Payload.Memory);
            var expired = await _directory.ExpireAsync(
                dto.ClusterName,
                dto.Now,
                cancellationToken).ConfigureAwait(false);

            return EncodeReply(session, request, new NodeExpireReply
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
