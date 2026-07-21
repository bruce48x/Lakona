using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Lakona.Game.Cluster
{
    /// <summary>
    /// Read-only compatibility view over replicated membership for APIs that still consume node records.
    /// </summary>
    public sealed class MembershipNodeDirectoryView : INodeDirectory
    {
        private readonly IClusterMembership membership;

        public MembershipNodeDirectoryView(IClusterMembership membership)
        {
            this.membership = membership ?? throw new ArgumentNullException(nameof(membership));
        }

        public ValueTask<NodeRecord?> ResolveAsync(
            string clusterName,
            NodeId node,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = membership.Current;
            for (var i = 0; i < snapshot.Members.Count; i++)
            {
                var member = snapshot.Members[i];
                if (member.Reference.Node == node && member.State == ClusterMemberState.Ready)
                {
                    return new ValueTask<NodeRecord?>(ToRecord(clusterName, member));
                }
            }

            return new ValueTask<NodeRecord?>((NodeRecord?)null);
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
            var records = membership.Current.Members
                .Where(member => Matches(member, query))
                .Select(member => ToRecord(query.ClusterName, member))
                .ToArray();
            return new ValueTask<IReadOnlyList<NodeRecord>>(records);
        }

        public ValueTask<NodeRegistrationResult> RegisterAsync(
            NodeRegistration registration,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) =>
            throw ReadOnly();

        public ValueTask<NodeHeartbeatStatus> HeartbeatAsync(
            string clusterName,
            NodeId node,
            long nodeEpoch,
            DateTimeOffset leaseExpiresAt,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) =>
            throw ReadOnly();

        public ValueTask<NodeStateUpdateStatus> UpdateStateAsync(
            string clusterName,
            NodeId node,
            long nodeEpoch,
            NodeState state,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) =>
            throw ReadOnly();

        public ValueTask<int> ExpireAsync(
            string clusterName,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) =>
            throw ReadOnly();

        private static bool Matches(ClusterMember member, NodeDirectoryQuery query)
        {
            var expectedState = query.State switch
            {
                NodeState.Ready => ClusterMemberState.Ready,
                NodeState.Draining => ClusterMemberState.Draining,
                _ => (ClusterMemberState?)null
            };
            if (expectedState is not null && member.State != expectedState.Value)
            {
                return false;
            }

            if (query.ActorHostName is not null && !member.ActorHosts.Any(host =>
                string.Equals(host.Actor, query.ActorHostName, StringComparison.Ordinal)
                && (query.ActorHostPolicyHash is null
                    || string.Equals(host.PolicyHash, query.ActorHostPolicyHash, StringComparison.Ordinal))))
            {
                return false;
            }

            if (query.StartupActorName is not null && !member.StartupActors.Any(startup =>
                string.Equals(startup.Actor, query.StartupActorName, StringComparison.Ordinal)
                && (query.StartupActorPolicyHash is null
                    || string.Equals(startup.PolicyHash, query.StartupActorPolicyHash, StringComparison.Ordinal))))
            {
                return false;
            }

            foreach (var label in query.Labels)
            {
                if (!member.Labels.TryGetValue(label.Key, out var value)
                    || !string.Equals(value, label.Value, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static NodeRecord ToRecord(string clusterName, ClusterMember member)
        {
            var state = member.State switch
            {
                ClusterMemberState.Ready => NodeState.Ready,
                ClusterMemberState.Draining => NodeState.Draining,
                ClusterMemberState.Fenced => NodeState.Dead,
                _ => NodeState.Starting
            };
            return new NodeRecord(
                clusterName,
                member.Reference.Node,
                0,
                new Dictionary<string, NodeEndpoint>(StringComparer.Ordinal)
                {
                    ["cluster"] = member.ClusterEndpoint
                },
                member.ActorHosts,
                member.StartupActors,
                member.Labels,
                state,
                DateTimeOffset.MaxValue,
                DateTimeOffset.MinValue);
        }

        private static InvalidOperationException ReadOnly() => new(
            "Replicated membership is the node authority; its compatibility directory view is read-only.");
    }
}
