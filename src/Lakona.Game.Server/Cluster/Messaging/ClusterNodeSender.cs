using System;
using System.Threading;
using System.Threading.Tasks;

namespace Lakona.Game.Cluster
{
    public sealed class ClusterNodeSender : IClusterNodeSender, IExactClusterNodeSender
    {
        private readonly IClusterMembership _membership;
        private readonly INodeMessenger _nodeMessenger;

        public ClusterNodeSender(
            IClusterMembership membership,
            INodeMessenger nodeMessenger)
        {
            _membership = membership ?? throw new ArgumentNullException(nameof(membership));
            _nodeMessenger = nodeMessenger ?? throw new ArgumentNullException(nameof(nodeMessenger));
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

            var snapshot = _membership.Current;
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

            var snapshot = _membership.Current;
            ClusterMember? targetMember = null;
            for (var i = 0; i < snapshot.Members.Count; i++)
            {
                var member = snapshot.Members[i];
                if (member.Reference.Node == nodeId && member.State == ClusterMemberState.Ready)
                {
                    if (targetMember is not null) return ClusterSendStatus.StaleRoute;
                    targetMember = member;
                }
            }
            if (targetMember is null) return ClusterSendStatus.StaleRoute;
            return await SendAsync(targetMember.Reference, snapshot.View, route, message, cancellationToken).ConfigureAwait(false);
        }
    }
}
