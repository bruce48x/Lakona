using System;
using Lakona.Game.Cluster;

namespace Lakona.Game.Cluster.Rpc
{
    public static class ULinkRpcClusterMessageConverter
    {
        public static ULinkRpcClusterSendRequest ToRequest(ClusterMessage message)
        {
            if (message is null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            return new ULinkRpcClusterSendRequest
            {
                Route = message.Route.Value,
                Kind = message.Kind,
                Payload = message.Payload.ToArray(),
                ExpiresAt = message.ExpiresAt,
                SourceNode = message.SourceNode.Value,
                CorrelationId = message.CorrelationId,
                TraceId = message.TraceId,
                OrderedBy = message.OrderedBy
            };
        }

        public static ClusterMessage ToClusterMessage(ULinkRpcClusterSendRequest request)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            return new ClusterMessage(
                request.Route,
                request.Kind,
                request.Payload ?? Array.Empty<byte>(),
                request.ExpiresAt,
                request.SourceNode,
                request.CorrelationId,
                request.TraceId,
                request.OrderedBy);
        }
    }
}
