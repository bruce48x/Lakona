using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Lakona.Game.Cluster
{
    public sealed class RouteLocation
    {
        private static readonly IReadOnlyDictionary<string, string> EmptyMetadata =
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal));

        public RouteLocation(
            RouteKey route,
            NodeId node,
            NodeEndpoint endpoint,
            DateTimeOffset expiresAt,
            long nodeEpoch = 0,
            long generation = 0,
            IReadOnlyDictionary<string, string>? metadata = null)
        {
            if (nodeEpoch < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(nodeEpoch), "Node epoch cannot be negative.");
            }

            if (generation < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(generation), "Route generation cannot be negative.");
            }

            Route = route;
            Node = node;
            Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            ExpiresAt = expiresAt;
            NodeEpoch = nodeEpoch;
            Generation = generation;
            Metadata = metadata is null
                ? EmptyMetadata
                : new ReadOnlyDictionary<string, string>(
                    new Dictionary<string, string>(metadata, StringComparer.Ordinal));
        }

        public RouteKey Route { get; }

        public NodeId Node { get; }

        public NodeEndpoint Endpoint { get; }

        public DateTimeOffset ExpiresAt { get; }

        public long NodeEpoch { get; }

        public long Generation { get; }

        public IReadOnlyDictionary<string, string> Metadata { get; }

        public bool IsExpired(DateTimeOffset now)
        {
            return now >= ExpiresAt;
        }

        public bool HasSameOwner(RouteLocation other)
        {
            if (other is null)
            {
                throw new ArgumentNullException(nameof(other));
            }

            return Route == other.Route
                && Node == other.Node
                && NodeEpoch == other.NodeEpoch
                && Generation == other.Generation;
        }

        public RouteLocation WithExpiresAt(DateTimeOffset expiresAt)
        {
            return new RouteLocation(
                Route,
                Node,
                Endpoint,
                expiresAt,
                NodeEpoch,
                Generation,
                Metadata);
        }
    }
}
