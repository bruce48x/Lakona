using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Lakona.Game.Cluster;

namespace Lakona.Game.Cluster.Rpc.Membership
{
    internal sealed class RpcClusterMembershipTransport : IClusterMembershipTransport
    {
        private readonly IClusterClientFactory clientFactory;
        private readonly TimeSpan requestTimeout;

        public RpcClusterMembershipTransport(IClusterClientFactory clientFactory)
            : this(
                clientFactory,
                TimeSpan.FromMilliseconds(
                    ClusterMembershipNodeOptions.DefaultRequestTimeoutMilliseconds))
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

            var started = Stopwatch.GetTimestamp();
            var outcome = "failure";
            using var activity = ClusterDiagnostics.StartActivity("cluster.membership.request");
            try
            {
                using (var requestCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    requestCancellation.CancelAfter(requestTimeout);
                    try
                    {
                        var client = await clientFactory
                            .GetClientAsync(endpoint, requestCancellation.Token)
                            .ConfigureAwait(false);
                        var reply = await client.CallAsync(
                            ClusterProtocol.MembershipFrameMethod,
                            new ClusterMembershipFrameRequest { Payload = request.Payload.ToArray() },
                            requestCancellation.Token).ConfigureAwait(false);
                        if (reply is null)
                            throw new InvalidOperationException("Membership RPC returned no reply.");

                        outcome = "success";
                        return new ClusterMembershipTransportFrame(reply.Payload);
                    }
                    catch (OperationCanceledException exception)
                        when (!cancellationToken.IsCancellationRequested
                            && requestCancellation.IsCancellationRequested)
                    {
                        outcome = "timeout";
                        throw new TimeoutException(
                            $"Membership RPC to '{endpoint.Address}' exceeded the {requestTimeout.TotalMilliseconds:0} ms request timeout.",
                            exception);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                outcome = "canceled";
                throw;
            }
            finally
            {
                activity?.SetTag("lakona.game.cluster.outcome", outcome);
                ClusterDiagnostics.RecordMembershipRequest(
                    outcome,
                    Stopwatch.GetElapsedTime(started));
            }
        }
    }
}
