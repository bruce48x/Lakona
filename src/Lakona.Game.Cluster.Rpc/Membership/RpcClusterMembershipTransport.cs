using System;
using System.Threading;
using System.Threading.Tasks;
using Lakona.Game.Cluster;

namespace Lakona.Game.Cluster.Rpc.Membership
{
    public sealed class RpcClusterMembershipTransport : IClusterMembershipTransport
    {
        private readonly IClusterClientFactory clientFactory;

        public RpcClusterMembershipTransport(IClusterClientFactory clientFactory)
        {
            this.clientFactory = clientFactory
                ?? throw new ArgumentNullException(nameof(clientFactory));
        }

        public async ValueTask<ClusterMembershipTransportFrame> RequestAsync(
            NodeEndpoint endpoint,
            ClusterMembershipTransportFrame request,
            CancellationToken cancellationToken = default)
        {
            if (endpoint is null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }

            var target = new RouteLocation(
                new RouteKey("cluster-membership:" + endpoint.Address),
                new NodeId("contact:" + endpoint.Address),
                endpoint,
                DateTimeOffset.MaxValue);
            var client = await clientFactory.GetClientAsync(target, cancellationToken)
                .ConfigureAwait(false);
            var reply = await client.CallAsync(
                ClusterProtocol.MembershipFrameMethod,
                new ClusterMembershipFrameRequest { Payload = request.Payload.ToArray() },
                cancellationToken).ConfigureAwait(false);
            if (reply is null)
            {
                throw new InvalidOperationException("Membership RPC returned no reply.");
            }

            return new ClusterMembershipTransportFrame(reply.Payload);
        }
    }
}
