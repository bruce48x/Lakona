using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Lakona.Game.Cluster
{
    public sealed class InMemoryNodeDirectory : INodeDirectory
    {
        private readonly object _gate = new object();
        private readonly Dictionary<(string ClusterName, NodeId NodeId), NodeRecord> _nodes =
            new Dictionary<(string ClusterName, NodeId NodeId), NodeRecord>();

        public ValueTask<NodeRegistrationResult> RegisterAsync(
            NodeRegistration registration,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            if (registration is null)
            {
                throw new ArgumentNullException(nameof(registration));
            }

            cancellationToken.ThrowIfCancellationRequested();

            lock (_gate)
            {
                var key = Key(registration.ClusterName, registration.NodeId);
                var epoch = _nodes.TryGetValue(key, out var existing)
                    ? existing.NodeEpoch + 1
                    : 1;
                var record = new NodeRecord(
                    registration.ClusterName,
                    registration.NodeId,
                    epoch,
                    registration.Endpoints,
                    registration.Services,
                    registration.Labels,
                    registration.State,
                    registration.LeaseExpiresAt,
                    now);

                _nodes[key] = record;
                return new ValueTask<NodeRegistrationResult>(
                    new NodeRegistrationResult(NodeRegistrationStatus.Registered, record));
            }
        }

        public ValueTask<NodeHeartbeatStatus> HeartbeatAsync(
            string clusterName,
            NodeId node,
            long nodeEpoch,
            DateTimeOffset leaseExpiresAt,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            if (nodeEpoch < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(nodeEpoch), "Node epoch cannot be negative.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            lock (_gate)
            {
                var status = TryGetCurrent(clusterName, node, nodeEpoch, now, out var current);
                if (status != NodeAccessStatus.Current)
                {
                    return new ValueTask<NodeHeartbeatStatus>(ToHeartbeatStatus(status));
                }

                _nodes[Key(clusterName, node)] = WithLease(current!, leaseExpiresAt, now);
                return new ValueTask<NodeHeartbeatStatus>(NodeHeartbeatStatus.Refreshed);
            }
        }

        public ValueTask<NodeStateUpdateStatus> UpdateStateAsync(
            string clusterName,
            NodeId node,
            long nodeEpoch,
            NodeState state,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            if (nodeEpoch < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(nodeEpoch), "Node epoch cannot be negative.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            lock (_gate)
            {
                var status = TryGetCurrent(clusterName, node, nodeEpoch, now, out var current);
                if (status != NodeAccessStatus.Current)
                {
                    return new ValueTask<NodeStateUpdateStatus>(ToStateUpdateStatus(status));
                }

                _nodes[Key(clusterName, node)] = WithState(current!, state, now);
                return new ValueTask<NodeStateUpdateStatus>(NodeStateUpdateStatus.Updated);
            }
        }

        public ValueTask<NodeRecord?> ResolveAsync(
            string clusterName,
            NodeId node,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_gate)
            {
                if (!_nodes.TryGetValue(Key(clusterName, node), out var record))
                {
                    return new ValueTask<NodeRecord?>((NodeRecord?)null);
                }

                if (record.State == NodeState.Dead || record.IsExpired(now))
                {
                    return new ValueTask<NodeRecord?>((NodeRecord?)null);
                }

                return new ValueTask<NodeRecord?>(record);
            }
        }

        public ValueTask<IReadOnlyList<NodeRecord>> QueryAsync(
            NodeDirectoryQuery query,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            if (query is null)
            {
                throw new ArgumentNullException(nameof(query));
            }

            cancellationToken.ThrowIfCancellationRequested();

            lock (_gate)
            {
                var records = _nodes.Values
                    .Where(record => MatchesQuery(record, query, now))
                    .OrderBy(record => record.NodeId.Value, StringComparer.Ordinal)
                    .ToArray();

                return new ValueTask<IReadOnlyList<NodeRecord>>(records);
            }
        }

        public ValueTask<int> ExpireAsync(
            string clusterName,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_gate)
            {
                var expired = _nodes
                    .Where(node =>
                        string.Equals(node.Key.ClusterName, clusterName, StringComparison.Ordinal)
                        && node.Value.State != NodeState.Dead
                        && node.Value.IsExpired(now))
                    .Select(node => node.Key)
                    .ToArray();

                foreach (var key in expired)
                {
                    _nodes[key] = WithState(_nodes[key], NodeState.Dead, now);
                }

                return new ValueTask<int>(expired.Length);
            }
        }

        private NodeAccessStatus TryGetCurrent(
            string clusterName,
            NodeId node,
            long nodeEpoch,
            DateTimeOffset now,
            out NodeRecord? record)
        {
            if (!_nodes.TryGetValue(Key(clusterName, node), out record))
            {
                return NodeAccessStatus.NotFound;
            }

            if (record.NodeEpoch != nodeEpoch)
            {
                return NodeAccessStatus.EpochMismatch;
            }

            if (record.State == NodeState.Dead || record.IsExpired(now))
            {
                return NodeAccessStatus.Expired;
            }

            return NodeAccessStatus.Current;
        }

        private static bool MatchesQuery(
            NodeRecord record,
            NodeDirectoryQuery query,
            DateTimeOffset now)
        {
            if (!string.Equals(record.ClusterName, query.ClusterName, StringComparison.Ordinal))
            {
                return false;
            }

            if (!query.IncludeExpired && (record.State == NodeState.Dead || record.IsExpired(now)))
            {
                return false;
            }

            if (query.ServiceKind is not null && !record.HasService(query.ServiceKind, query.ServiceName))
            {
                return false;
            }

            if (query.ServiceKind is null && query.ServiceName is not null
                && !record.Services.Any(service => string.Equals(service.Name, query.ServiceName, StringComparison.Ordinal)))
            {
                return false;
            }

            if (query.State is not null && record.State != query.State.Value)
            {
                return false;
            }

            foreach (var label in query.Labels)
            {
                if (!record.Labels.TryGetValue(label.Key, out var value)
                    || !string.Equals(value, label.Value, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static NodeRecord WithLease(
            NodeRecord record,
            DateTimeOffset leaseExpiresAt,
            DateTimeOffset updatedAt)
        {
            return new NodeRecord(
                record.ClusterName,
                record.NodeId,
                record.NodeEpoch,
                record.Endpoints,
                record.Services,
                record.Labels,
                record.State,
                leaseExpiresAt,
                updatedAt);
        }

        private static NodeRecord WithState(
            NodeRecord record,
            NodeState state,
            DateTimeOffset updatedAt)
        {
            return new NodeRecord(
                record.ClusterName,
                record.NodeId,
                record.NodeEpoch,
                record.Endpoints,
                record.Services,
                record.Labels,
                state,
                record.LeaseExpiresAt,
                updatedAt);
        }

        private static NodeHeartbeatStatus ToHeartbeatStatus(NodeAccessStatus status)
        {
            switch (status)
            {
                case NodeAccessStatus.NotFound:
                    return NodeHeartbeatStatus.NodeNotFound;
                case NodeAccessStatus.EpochMismatch:
                    return NodeHeartbeatStatus.EpochMismatch;
                case NodeAccessStatus.Expired:
                    return NodeHeartbeatStatus.Expired;
                default:
                    return NodeHeartbeatStatus.Refreshed;
            }
        }

        private static NodeStateUpdateStatus ToStateUpdateStatus(NodeAccessStatus status)
        {
            switch (status)
            {
                case NodeAccessStatus.NotFound:
                    return NodeStateUpdateStatus.NodeNotFound;
                case NodeAccessStatus.EpochMismatch:
                    return NodeStateUpdateStatus.EpochMismatch;
                case NodeAccessStatus.Expired:
                    return NodeStateUpdateStatus.Expired;
                default:
                    return NodeStateUpdateStatus.Updated;
            }
        }

        private static (string ClusterName, NodeId NodeId) Key(string clusterName, NodeId node)
        {
            return (clusterName, node);
        }

        private enum NodeAccessStatus
        {
            Current,
            NotFound,
            EpochMismatch,
            Expired
        }
    }
}
