using System;
using System.Threading;
using System.Threading.Tasks;
using Lakona.Rpc.Server;

namespace Lakona.Game.Cluster.Rpc.Membership
{
    internal sealed class ClusterMembershipFrameBinder
    {
        private readonly IClusterMembershipFrameHandler handler;

        public ClusterMembershipFrameBinder(IClusterMembershipFrameHandler handler)
        {
            this.handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public void Bind(RpcServiceRegistry registry)
        {
            var service = registry.RegisterSingleton(
                ClusterProtocol.ServiceId,
                this,
                serviceName: nameof(ClusterMembershipFrameBinder));
            service.Register<ClusterMembershipFrameRequest, ClusterMembershipFrameReply>(
                ClusterProtocol.Methods.MembershipFrame.Id,
                static (binder, request, cancellationToken) =>
                    binder.HandleAsync(request, cancellationToken),
                methodName: nameof(HandleAsync));
        }

        public static void Bind(
            RpcServiceRegistry registry,
            IClusterMembershipFrameHandler handler)
        {
            new ClusterMembershipFrameBinder(handler).Bind(registry);
        }

        private async ValueTask<ClusterMembershipFrameReply> HandleAsync(
            ClusterMembershipFrameRequest request,
            CancellationToken cancellationToken)
        {
            if (request is null || request.Payload is null)
            {
                throw new RpcBadRequestException("Cluster membership request payload is required.");
            }

            var response = await handler.HandleAsync(
                new ClusterMembershipTransportFrame(request.Payload),
                cancellationToken).ConfigureAwait(false);
            return new ClusterMembershipFrameReply { Payload = response.Payload.ToArray() };
        }
    }
}
