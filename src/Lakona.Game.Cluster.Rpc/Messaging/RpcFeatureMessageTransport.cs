using System;
using System.Threading;
using System.Threading.Tasks;
using Lakona.Game.Cluster;

namespace Lakona.Game.Cluster.Rpc
{
    public sealed class RpcFeatureMessageTransport : IFeatureMessageTransport
    {
        private readonly IClusterClientFactory _clientFactory;

        public RpcFeatureMessageTransport(IClusterClientFactory clientFactory)
        {
            _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        }

        public async ValueTask<FeatureMessageReply> SendAsync(
            ClusterNodeDescriptor target,
            FeatureMessageRequest request,
            CancellationToken cancellationToken = default)
        {
            if (target is null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (!target.Endpoints.TryGetValue("cluster", out var endpoint))
            {
                return new FeatureMessageReply(ClusterSendStatus.NodeUnavailable, Array.Empty<byte>());
            }

            var routeLocation = new RouteLocation(
                new RouteKey($"feature:{request.Feature.Value}"),
                target.Node,
                endpoint,
                request.ExpiresAt);

            try
            {
                var client = await _clientFactory.GetClientAsync(routeLocation, cancellationToken)
                    .ConfigureAwait(false);
                var reply = await client.CallAsync(
                    ClusterProtocol.FeatureMessageMethod,
                    FeatureMessageConverter.ToRpcRequest(request),
                    cancellationToken).ConfigureAwait(false);
                return FeatureMessageConverter.ToFeatureReply(reply);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TimeoutException ex)
            {
                return new FeatureMessageReply(ClusterSendStatus.Timeout, Array.Empty<byte>(), ex.Message);
            }
            catch (Exception ex)
            {
                return new FeatureMessageReply(ClusterSendStatus.Failed, Array.Empty<byte>(), ex.Message);
            }
        }
    }
}
