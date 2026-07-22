using System;
using System.Threading;
using System.Threading.Tasks;
using Lakona.Game.Cluster;
using Lakona.Rpc.Server;

namespace Lakona.Game.Cluster.Rpc
{
    public sealed class ClusterMessageBinder
    {
        private readonly IClusterMessageHandler _handler;
        private readonly IClusterMembership? membership;
        private readonly NodeId? localNode;

        public ClusterMessageBinder(IClusterMessageHandler handler)
            : this(handler, null, null)
        {
        }

        public ClusterMessageBinder(
            IClusterMessageHandler handler,
            IClusterMembership? membership,
            NodeId? localNode)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
            this.membership = membership;
            this.localNode = localNode;
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
                serviceName: nameof(ClusterMessageBinder));
            service.Register<ClusterSendRequest, ClusterSendReply>(
                ClusterProtocol.SendMethodId,
                static (binder, request, cancellationToken) =>
                    binder.HandleAsync(request, cancellationToken),
                methodName: nameof(HandleAsync));
        }

        public static void Bind(
            RpcServiceRegistry registry,
            IClusterMessageHandler handler)
        {
            new ClusterMessageBinder(handler).Bind(registry);
        }

        public static void Bind(
            RpcServiceRegistry registry,
            IClusterMessageHandler handler,
            IClusterMembership membership,
            NodeId localNode)
        {
            new ClusterMessageBinder(handler, membership, localNode).Bind(registry);
        }

        private async ValueTask<ClusterSendReply> HandleAsync(
            ClusterSendRequest request,
            CancellationToken cancellationToken)
        {
            if (!AcceptsExactTarget(request))
            {
                return new ClusterSendReply
                {
                    Status = (int)ClusterSendStatus.StaleRoute
                };
            }

            var status = await _handler.HandleAsync(
                ClusterMessageConverter.ToClusterMessage(request),
                cancellationToken).ConfigureAwait(false);

            return new ClusterSendReply
            {
                Status = (int)status
            };
        }

        private bool AcceptsExactTarget(ClusterSendRequest request)
        {
            var hasAnyExactField = request.TargetClusterIncarnation is not null
                || request.TargetNode is not null
                || request.TargetNodeIncarnation is not null
                || request.TargetMembershipView is not null;
            if (!hasAnyExactField)
            {
                return true;
            }

            if (membership is null
                || localNode is null
                || request.TargetClusterIncarnation is not Guid cluster
                || string.IsNullOrWhiteSpace(request.TargetNode)
                || request.TargetNodeIncarnation is not Guid incarnation
                || request.TargetMembershipView is not long view
                || view <= 0
                || request.TargetNode != localNode.Value.Value)
            {
                return false;
            }

            var snapshot = membership.Current;
            var target = new NodeReference(
                new ClusterIncarnationId(cluster),
                new NodeId(request.TargetNode),
                new NodeIncarnationId(incarnation));
            return snapshot.Cluster == target.Cluster
                && snapshot.View.Value >= view
                && snapshot.TryGetMember(target, out var member)
                && member is not null
                && member.State is ClusterMemberState.Ready or ClusterMemberState.Recovering;
        }
    }
}
