using System;
using Lakona.Game.Cluster;

namespace Lakona.Game.Cluster.Rpc
{
    public static class ClusterMessageConverter
    {
        public static ClusterSendRequest ToRequest(ClusterMessage message)
        {
            if (message is null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            return new ClusterSendRequest
            {
                Route = message.Route.Value,
                Kind = message.Kind,
                Payload = message.Payload.ToArray(),
                ExpiresAt = message.ExpiresAt,
                SourceNode = message.SourceNode.Value,
                CorrelationId = message.CorrelationId,
                TraceId = message.TraceId,
                OrderedBy = message.OrderedBy,
                Metadata = message.Metadata.Count == 0
                    ? null
                    : new System.Collections.Generic.Dictionary<string, string>(message.Metadata, StringComparer.Ordinal)
            };
        }

        public static ClusterSendRequest ToRequest(
            RouteLocation target,
            ClusterMessage message)
        {
            if (target is null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            var request = ToRequest(message);
            if (target.NodeReference is not null)
            {
                request.TargetClusterIncarnation = target.NodeReference.Cluster.Value;
                request.TargetNode = target.NodeReference.Node.Value;
                request.TargetNodeIncarnation = target.NodeReference.Incarnation.Value;
                request.TargetMembershipView = target.MembershipView.Value;
            }

            return request;
        }

        public static ClusterMessage ToClusterMessage(ClusterSendRequest request)
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
                request.OrderedBy,
                request.Metadata);
        }
    }
}
