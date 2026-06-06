using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lakona.Game.Cluster;
using Lakona.Rpc.Core;

namespace Lakona.Game.Cluster.Rpc
{
    public sealed class ULinkRpcNodeDirectory : INodeDirectory
    {
        private readonly IRpcClient _client;

        public ULinkRpcNodeDirectory(IRpcClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public async ValueTask<NodeRegistrationResult> RegisterAsync(
            NodeRegistration registration,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            if (registration is null)
            {
                throw new ArgumentNullException(nameof(registration));
            }

            var reply = await _client.CallAsync(
                ULinkRpcClusterProtocol.RegisterNodeMethod,
                new ULinkRpcNodeRegisterRequest
                {
                    Registration = ULinkRpcNodeDirectoryRecordConverter.ToDto(registration),
                    Now = now
                },
                cancellationToken).ConfigureAwait(false);

            var status = ToRegistrationStatus(reply.Status);
            if (status != NodeRegistrationStatus.Registered)
            {
                return new NodeRegistrationResult(status, null);
            }

            if (reply.Record is null)
            {
                return new NodeRegistrationResult(NodeRegistrationStatus.InvalidRegistration, null);
            }

            return new NodeRegistrationResult(
                NodeRegistrationStatus.Registered,
                ULinkRpcNodeDirectoryRecordConverter.ToNodeRecord(reply.Record));
        }

        public async ValueTask<NodeHeartbeatStatus> HeartbeatAsync(
            string clusterName,
            NodeId node,
            long nodeEpoch,
            DateTimeOffset leaseExpiresAt,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            var reply = await _client.CallAsync(
                ULinkRpcClusterProtocol.HeartbeatNodeMethod,
                new ULinkRpcNodeHeartbeatRequest
                {
                    ClusterName = clusterName,
                    Node = node.Value,
                    NodeEpoch = nodeEpoch,
                    LeaseExpiresAt = leaseExpiresAt,
                    Now = now
                },
                cancellationToken).ConfigureAwait(false);

            return ToHeartbeatStatus(reply.Status);
        }

        public async ValueTask<NodeStateUpdateStatus> UpdateStateAsync(
            string clusterName,
            NodeId node,
            long nodeEpoch,
            NodeState state,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            var reply = await _client.CallAsync(
                ULinkRpcClusterProtocol.UpdateNodeStateMethod,
                new ULinkRpcNodeUpdateStateRequest
                {
                    ClusterName = clusterName,
                    Node = node.Value,
                    NodeEpoch = nodeEpoch,
                    State = (int)state,
                    Now = now
                },
                cancellationToken).ConfigureAwait(false);

            return ToStateUpdateStatus(reply.Status);
        }

        public async ValueTask<NodeRecord?> ResolveAsync(
            string clusterName,
            NodeId node,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            var reply = await _client.CallAsync(
                ULinkRpcClusterProtocol.ResolveNodeMethod,
                new ULinkRpcNodeResolveRequest
                {
                    ClusterName = clusterName,
                    Node = node.Value,
                    Now = now
                },
                cancellationToken).ConfigureAwait(false);

            return reply.Record is null
                ? null
                : ULinkRpcNodeDirectoryRecordConverter.ToNodeRecord(reply.Record);
        }

        public async ValueTask<IReadOnlyList<NodeRecord>> QueryAsync(
            NodeDirectoryQuery query,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            if (query is null)
            {
                throw new ArgumentNullException(nameof(query));
            }

            var reply = await _client.CallAsync(
                ULinkRpcClusterProtocol.QueryNodesMethod,
                new ULinkRpcNodeQueryRequest
                {
                    Query = ULinkRpcNodeDirectoryRecordConverter.ToDto(query),
                    Now = now
                },
                cancellationToken).ConfigureAwait(false);

            if (reply.Records is null || reply.Records.Count == 0)
            {
                return Array.Empty<NodeRecord>();
            }

            var records = new List<NodeRecord>(reply.Records.Count);
            for (var i = 0; i < reply.Records.Count; i++)
            {
                records.Add(ULinkRpcNodeDirectoryRecordConverter.ToNodeRecord(reply.Records[i]));
            }

            return records;
        }

        public async ValueTask<int> ExpireAsync(
            string clusterName,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            var reply = await _client.CallAsync(
                ULinkRpcClusterProtocol.ExpireNodesMethod,
                new ULinkRpcNodeExpireRequest
                {
                    ClusterName = clusterName,
                    Now = now
                },
                cancellationToken).ConfigureAwait(false);

            return reply.Expired;
        }

        private static NodeRegistrationStatus ToRegistrationStatus(int status)
        {
            return Enum.IsDefined(typeof(NodeRegistrationStatus), status)
                ? (NodeRegistrationStatus)status
                : NodeRegistrationStatus.InvalidRegistration;
        }

        private static NodeHeartbeatStatus ToHeartbeatStatus(int status)
        {
            return Enum.IsDefined(typeof(NodeHeartbeatStatus), status)
                ? (NodeHeartbeatStatus)status
                : NodeHeartbeatStatus.EpochMismatch;
        }

        private static NodeStateUpdateStatus ToStateUpdateStatus(int status)
        {
            return Enum.IsDefined(typeof(NodeStateUpdateStatus), status)
                ? (NodeStateUpdateStatus)status
                : NodeStateUpdateStatus.EpochMismatch;
        }
    }
}
