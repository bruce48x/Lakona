using System;
using System.Threading;
using System.Threading.Tasks;

namespace Lakona.Game.Cluster
{
    public sealed class ClusterNodeSender : IClusterNodeSender, IExactClusterNodeSender
    {
        private readonly INodeDirectory _nodeDirectory;
        private readonly IClusterMembership? _membership;
        private readonly INodeMessenger _nodeMessenger;
        private readonly ClusterNodeSenderOptions _options;

        public ClusterNodeSender(
            INodeDirectory nodeDirectory,
            INodeMessenger nodeMessenger,
            ClusterNodeSenderOptions? options = null)
        {
            _nodeDirectory = nodeDirectory ?? throw new ArgumentNullException(nameof(nodeDirectory));
            _nodeMessenger = nodeMessenger ?? throw new ArgumentNullException(nameof(nodeMessenger));
            _options = options ?? new ClusterNodeSenderOptions();
        }

        internal ClusterNodeSender(
            IClusterMembership membership,
            INodeMessenger nodeMessenger)
        {
            _nodeDirectory = null!;
            _membership = membership ?? throw new ArgumentNullException(nameof(membership));
            _nodeMessenger = nodeMessenger ?? throw new ArgumentNullException(nameof(nodeMessenger));
            _options = new ClusterNodeSenderOptions();
        }

        public async ValueTask<ClusterSendStatus> SendAsync(
            NodeReference target,
            MembershipViewId view,
            RouteKey route,
            ClusterMessage message,
            CancellationToken cancellationToken = default)
        {
            if (target is null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (message is null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            var membership = _membership ?? throw new InvalidOperationException(
                "Exact node sends require a membership-backed ClusterNodeSender.");
            var snapshot = membership.Current;
            if (snapshot.Cluster != target.Cluster
                || snapshot.View != view
                || !snapshot.TryGetMember(target, out var member)
                || member is null
                || member.State != ClusterMemberState.Ready)
            {
                return ClusterSendStatus.StaleRoute;
            }

            return await _nodeMessenger.SendAsync(
                new RouteLocation(route, target, view, member.ClusterEndpoint),
                message,
                cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask<ClusterSendStatus> SendAsync(
            NodeId nodeId,
            long? expectedNodeEpoch,
            RouteKey route,
            ClusterMessage message,
            CancellationToken cancellationToken = default)
        {
            if (message is null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            _options.Validate();

            if (_membership is not null)
            {
                var snapshot = _membership.Current;
                ClusterMember? targetMember = null;
                for (var i = 0; i < snapshot.Members.Count; i++)
                {
                    var member = snapshot.Members[i];
                    if (member.Reference.Node == nodeId
                        && member.State == ClusterMemberState.Ready)
                    {
                        if (targetMember is not null)
                        {
                            return ClusterSendStatus.StaleRoute;
                        }

                        targetMember = member;
                    }
                }

                if (targetMember is null)
                {
                    return ClusterSendStatus.StaleRoute;
                }

                return await SendAsync(
                    targetMember.Reference,
                    snapshot.View,
                    route,
                    message,
                    cancellationToken).ConfigureAwait(false);
            }

            var now = DateTimeOffset.UtcNow;
            var record = await _nodeDirectory.ResolveAsync(
                _options.ClusterName,
                nodeId,
                now,
                cancellationToken).ConfigureAwait(false);

            if (record is null || record.IsExpired(now))
            {
                return ClusterSendStatus.StaleRoute;
            }

            if (expectedNodeEpoch is not null && record.NodeEpoch != expectedNodeEpoch.Value)
            {
                return ClusterSendStatus.NodeEpochMismatch;
            }

            if (!record.Endpoints.TryGetValue(_options.EndpointName, out var endpoint))
            {
                return ClusterSendStatus.HandlerUnavailable;
            }

            var target = new RouteLocation(
                route,
                nodeId,
                endpoint,
                record.LeaseExpiresAt,
                record.NodeEpoch);

            return await _nodeMessenger.SendAsync(
                target,
                message,
                cancellationToken).ConfigureAwait(false);
        }
    }
}
