using System;
using System.Threading;
using System.Threading.Tasks;
using Lakona.Game.Cluster;

namespace Lakona.Game.Cluster.Rpc.Membership
{
    public sealed class RpcClusterMembershipTransport : IClusterMembershipTransport
    {
        private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(2);
        private readonly IClusterClientFactory clientFactory;
        private readonly TimeSpan requestTimeout;

        public RpcClusterMembershipTransport(IClusterClientFactory clientFactory)
            : this(clientFactory, DefaultRequestTimeout)
        {
        }

        public RpcClusterMembershipTransport(
            IClusterClientFactory clientFactory,
            TimeSpan requestTimeout)
        {
            this.clientFactory = clientFactory
                ?? throw new ArgumentNullException(nameof(clientFactory));
            if (requestTimeout <= TimeSpan.Zero
                || requestTimeout > TimeSpan.FromMilliseconds(int.MaxValue))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(requestTimeout),
                    requestTimeout,
                    "Membership RPC request timeout must be positive and finite.");
            }

            this.requestTimeout = requestTimeout;
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

            using (var requestCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                requestCancellation.CancelAfter(requestTimeout);
                try
                {
                    var target = new RouteLocation(
                        new RouteKey("cluster-membership:" + endpoint.Address),
                        new NodeId("contact:" + endpoint.Address),
                        endpoint,
                        DateTimeOffset.MaxValue);
                    var client = await clientFactory
                        .GetClientAsync(target, requestCancellation.Token)
                        .ConfigureAwait(false);
                    var reply = await client.CallAsync(
                        ClusterProtocol.MembershipFrameMethod,
                        new ClusterMembershipFrameRequest { Payload = request.Payload.ToArray() },
                        requestCancellation.Token).ConfigureAwait(false);
                    if (reply is null)
                    {
                        throw new InvalidOperationException("Membership RPC returned no reply.");
                    }

                    return new ClusterMembershipTransportFrame(reply.Payload);
                }
                catch (OperationCanceledException exception)
                    when (!cancellationToken.IsCancellationRequested
                        && requestCancellation.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        $"Membership RPC to '{endpoint.Address}' exceeded the {requestTimeout.TotalMilliseconds:0} ms request timeout.",
                        exception);
                }
            }
        }
    }
}
