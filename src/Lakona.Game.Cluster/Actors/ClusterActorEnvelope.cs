using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Lakona.Game.Cluster
{
    public sealed class ClusterActorEnvelope
    {
        public const string ReplyCorrelationMetadataKey = "lakona-game.cluster.actor.reply_correlation";

        private static readonly IReadOnlyDictionary<string, string> EmptyMetadata =
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal));

        public ClusterActorEnvelope(
            RouteKey route,
            string actorId,
            string kind,
            ReadOnlyMemory<byte> payload,
            DateTimeOffset expiresAt,
            NodeId sourceNode,
            string? correlationId = null,
            string? traceId = null,
            string? replyCorrelationId = null,
            string? orderedBy = null,
            IReadOnlyDictionary<string, string>? metadata = null)
        {
            if (string.IsNullOrWhiteSpace(actorId))
            {
                throw new ArgumentException("Actor id is required.", nameof(actorId));
            }

            if (string.IsNullOrWhiteSpace(kind))
            {
                throw new ArgumentException("Actor message kind is required.", nameof(kind));
            }

            Route = route;
            ActorId = actorId;
            Kind = kind;
            Payload = payload.ToArray();
            ExpiresAt = expiresAt;
            SourceNode = sourceNode;
            CorrelationId = correlationId;
            TraceId = traceId;
            ReplyCorrelationId = replyCorrelationId;
            OrderedBy = orderedBy;
            Metadata = metadata is null
                ? EmptyMetadata
                : new ReadOnlyDictionary<string, string>(
                    new Dictionary<string, string>(metadata, StringComparer.Ordinal));
        }

        public RouteKey Route { get; }

        public string ActorId { get; }

        public string Kind { get; }

        public ReadOnlyMemory<byte> Payload { get; }

        public DateTimeOffset ExpiresAt { get; }

        public NodeId SourceNode { get; }

        public string? CorrelationId { get; }

        public string? TraceId { get; }

        public string? ReplyCorrelationId { get; }

        public string? OrderedBy { get; }

        public IReadOnlyDictionary<string, string> Metadata { get; }

        public ClusterMessage ToClusterMessage()
        {
            var metadata = new Dictionary<string, string>(Metadata, StringComparer.Ordinal);
            if (ReplyCorrelationId is not null)
            {
                metadata[ReplyCorrelationMetadataKey] = ReplyCorrelationId;
            }

            return new ClusterMessage(
                Route,
                Kind,
                Payload,
                ExpiresAt,
                SourceNode,
                CorrelationId,
                TraceId,
                OrderedBy,
                metadata);
        }

        public static bool TryFromClusterMessage(
            ClusterMessage message,
            out ClusterActorEnvelope? envelope)
        {
            if (message is null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            if (!ClusterActorRouteKeys.TryGetActorId(message.Route, out var actorId))
            {
                envelope = null;
                return false;
            }

            message.Metadata.TryGetValue(ReplyCorrelationMetadataKey, out var replyCorrelationId);
            var metadata = new Dictionary<string, string>(message.Metadata, StringComparer.Ordinal);
            metadata.Remove(ReplyCorrelationMetadataKey);

            envelope = new ClusterActorEnvelope(
                message.Route,
                actorId,
                message.Kind,
                message.Payload,
                message.ExpiresAt,
                message.SourceNode,
                message.CorrelationId,
                message.TraceId,
                replyCorrelationId,
                message.OrderedBy,
                metadata);
            return true;
        }
    }
}
