using System;
using System.Threading;
using System.Threading.Tasks;
using Lakona.Rpc.Core;
using Lakona.Rpc.Server;

namespace Lakona.Game.Cluster.Rpc.Membership
{
    public sealed class ClusterMembershipFrameBinder
    {
        private readonly IClusterMembershipFrameHandler handler;

        public ClusterMembershipFrameBinder(IClusterMembershipFrameHandler handler)
        {
            this.handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public void Bind(RpcServiceRegistry registry)
        {
            registry.Register(
                ClusterProtocol.ServiceId,
                ClusterProtocol.MembershipFrameMethodId,
                HandleAsync);
        }

        public static void Bind(
            RpcServiceRegistry registry,
            IClusterMembershipFrameHandler handler)
        {
            new ClusterMembershipFrameBinder(handler).Bind(registry);
        }

        private async ValueTask<TransportFrame> HandleAsync(
            RpcSession session,
            RpcRequestFrame request,
            CancellationToken cancellationToken)
        {
            var dto = session.Serializer.Deserialize<ClusterMembershipFrameRequest>(
                request.Payload.Memory);
            var response = await handler.HandleAsync(
                new ClusterMembershipTransportFrame(dto.Payload),
                cancellationToken).ConfigureAwait(false);
            using var payload = session.Serializer.SerializeFrame(
                new ClusterMembershipFrameReply { Payload = response.Payload.ToArray() });
            return RpcEnvelopeCodec.EncodeResponse(request.RequestId, RpcStatus.Ok, payload.Memory);
        }
    }
}
