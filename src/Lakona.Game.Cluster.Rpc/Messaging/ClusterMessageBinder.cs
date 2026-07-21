using System;
using System.Threading;
using System.Threading.Tasks;
using Lakona.Game.Cluster;
using Lakona.Rpc.Core;
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

            registry.Register(
                ClusterProtocol.ServiceId,
                ClusterProtocol.SendMethodId,
                HandleAsync);
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

        private async ValueTask<TransportFrame> HandleAsync(
            RpcSession session,
            RpcRequestFrame request,
            CancellationToken cancellationToken)
        {
            var dto = session.Serializer.Deserialize<ClusterSendRequest>(request.Payload.Memory);
            if (!AcceptsExactTarget(dto))
            {
                using var fencedPayload = session.Serializer.SerializeFrame(new ClusterSendReply
                {
                    Status = (int)ClusterSendStatus.StaleRoute
                });
                return RpcEnvelopeCodec.EncodeResponse(
                    request.RequestId,
                    RpcStatus.Ok,
                    fencedPayload.Memory);
            }

            var status = await _handler.HandleAsync(
                ClusterMessageConverter.ToClusterMessage(dto),
                cancellationToken).ConfigureAwait(false);

            using var payload = session.Serializer.SerializeFrame(new ClusterSendReply
            {
                Status = (int)status
            });
            return RpcEnvelopeCodec.EncodeResponse(request.RequestId, RpcStatus.Ok, payload.Memory);
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
