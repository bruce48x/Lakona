using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lakona.Game.Cluster;
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

            var service = registry.RegisterSingleton(
                ClusterProtocol.ServiceId,
                this,
                serviceName: nameof(NodeDirectoryBinder));
            service.Register<NodeRegisterRequest, NodeRegisterReply>(ClusterProtocol.RegisterNodeMethodId, static (binder, request, cancellationToken) => binder.RegisterAsync(request, cancellationToken), methodName: nameof(RegisterAsync));
            service.Register<NodeHeartbeatRequest, NodeHeartbeatReply>(ClusterProtocol.HeartbeatNodeMethodId, static (binder, request, cancellationToken) => binder.HeartbeatAsync(request, cancellationToken), methodName: nameof(HeartbeatAsync));
            service.Register<NodeUpdateStateRequest, NodeUpdateStateReply>(ClusterProtocol.UpdateNodeStateMethodId, static (binder, request, cancellationToken) => binder.UpdateStateAsync(request, cancellationToken), methodName: nameof(UpdateStateAsync));
            service.Register<NodeResolveRequest, NodeResolveReply>(ClusterProtocol.ResolveNodeMethodId, static (binder, request, cancellationToken) => binder.ResolveAsync(request, cancellationToken), methodName: nameof(ResolveAsync));
            service.Register<NodeQueryRequest, NodeQueryReply>(ClusterProtocol.QueryNodesMethodId, static (binder, request, cancellationToken) => binder.QueryAsync(request, cancellationToken), methodName: nameof(QueryAsync));
            service.Register<NodeExpireRequest, NodeExpireReply>(ClusterProtocol.ExpireNodesMethodId, static (binder, request, cancellationToken) => binder.ExpireAsync(request, cancellationToken), methodName: nameof(ExpireAsync));
        }

        public static void Bind(RpcServiceRegistry registry, INodeDirectory directory)
        {
            new NodeDirectoryBinder(directory).Bind(registry);
        }

        private async ValueTask<NodeRegisterReply> RegisterAsync(
            NodeRegisterRequest request,
            CancellationToken cancellationToken)
        {
            if (request.Registration is null)
            {
                throw new InvalidOperationException("Node registration is required.");
            }

            var result = await _directory.RegisterAsync(
                NodeDirectoryRecordConverter.ToNodeRegistration(request.Registration),
                request.Now,
                cancellationToken).ConfigureAwait(false);

            return new NodeRegisterReply
            {
                Status = (int)result.Status,
                Record = result.Record is null ? null : NodeDirectoryRecordConverter.ToDto(result.Record)
            };
        }

        private async ValueTask<NodeHeartbeatReply> HeartbeatAsync(
            NodeHeartbeatRequest request,
            CancellationToken cancellationToken)
        {
            var status = await _directory.HeartbeatAsync(
                request.ClusterName,
                request.Node,
                request.NodeEpoch,
                request.LeaseExpiresAt,
                request.Now,
                cancellationToken).ConfigureAwait(false);

            return new NodeHeartbeatReply
            {
                Status = (int)status
            };
        }

        private async ValueTask<NodeUpdateStateReply> UpdateStateAsync(
            NodeUpdateStateRequest request,
            CancellationToken cancellationToken)
        {
            var status = await _directory.UpdateStateAsync(
                request.ClusterName,
                request.Node,
                request.NodeEpoch,
                ToNodeState(request.State),
                request.Now,
                cancellationToken).ConfigureAwait(false);

            return new NodeUpdateStateReply
            {
                Status = (int)status
            };
        }

        private async ValueTask<NodeResolveReply> ResolveAsync(
            NodeResolveRequest request,
            CancellationToken cancellationToken)
        {
            var record = await _directory.ResolveAsync(
                request.ClusterName,
                request.Node,
                request.Now,
                cancellationToken).ConfigureAwait(false);

            return new NodeResolveReply
            {
                Record = record is null ? null : NodeDirectoryRecordConverter.ToDto(record)
            };
        }

        private async ValueTask<NodeQueryReply> QueryAsync(
            NodeQueryRequest request,
            CancellationToken cancellationToken)
        {
            if (request.Query is null)
            {
                throw new InvalidOperationException("Node directory query is required.");
            }

            var records = await _directory.QueryAsync(
                NodeDirectoryRecordConverter.ToNodeDirectoryQuery(request.Query),
                request.Now,
                cancellationToken).ConfigureAwait(false);

            var recordDtos = new List<NodeRecordDto>(records.Count);
            for (var i = 0; i < records.Count; i++)
            {
                recordDtos.Add(NodeDirectoryRecordConverter.ToDto(records[i]));
            }

            return new NodeQueryReply
            {
                Records = recordDtos
            };
        }

        private async ValueTask<NodeExpireReply> ExpireAsync(
            NodeExpireRequest request,
            CancellationToken cancellationToken)
        {
            var expired = await _directory.ExpireAsync(
                request.ClusterName,
                request.Now,
                cancellationToken).ConfigureAwait(false);

            return new NodeExpireReply
            {
                Expired = expired
            };
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
