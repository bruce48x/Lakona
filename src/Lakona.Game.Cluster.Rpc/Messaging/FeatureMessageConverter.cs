using Lakona.Game.Cluster;

namespace Lakona.Game.Cluster.Rpc
{
    internal static class FeatureMessageConverter
    {
        public static FeatureSendRequest ToRpcRequest(FeatureMessageRequest request)
        {
            return new FeatureSendRequest
            {
                Feature = request.Feature.Value,
                Kind = request.Kind,
                Payload = request.Payload.ToArray(),
                ExpiresAt = request.ExpiresAt,
                SourceNode = request.SourceNode.Value,
                CorrelationId = request.CorrelationId
            };
        }

        public static FeatureMessageRequest ToFeatureRequest(FeatureSendRequest request)
        {
            return new FeatureMessageRequest(
                new FeatureName(request.Feature),
                request.Kind,
                request.Payload,
                request.ExpiresAt,
                new NodeId(request.SourceNode),
                request.CorrelationId);
        }

        public static FeatureSendReply ToRpcReply(FeatureMessageReply reply)
        {
            return new FeatureSendReply
            {
                Status = (int)reply.Status,
                Payload = reply.Payload.ToArray(),
                ErrorMessage = reply.ErrorMessage
            };
        }

        public static FeatureMessageReply ToFeatureReply(FeatureSendReply? reply)
        {
            if (reply is null || !System.Enum.IsDefined(typeof(ClusterSendStatus), reply.Status))
            {
                return new FeatureMessageReply(ClusterSendStatus.Failed, System.Array.Empty<byte>());
            }

            return new FeatureMessageReply(
                (ClusterSendStatus)reply.Status,
                reply.Payload,
                reply.ErrorMessage);
        }
    }
}
