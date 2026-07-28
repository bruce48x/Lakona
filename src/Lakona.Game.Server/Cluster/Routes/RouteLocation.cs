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
            : this(
                route,
                node,
                endpoint,
                expiresAt,
                nodeEpoch,
                generation,
                metadata,
                null,
                default)
        {
        }

        public RouteLocation(
            RouteKey route,
            NodeReference nodeReference,
            MembershipViewId membershipView,
            NodeEndpoint endpoint,
            IReadOnlyDictionary<string, string>? metadata = null)
            : this(
                route,
                (nodeReference ?? throw new ArgumentNullException(nameof(nodeReference))).Node,
                endpoint,
                DateTimeOffset.MaxValue,
                0,
                0,
                metadata,
                nodeReference,
                membershipView)
        {
        }

        private RouteLocation(
            RouteKey route,
            NodeId node,
            NodeEndpoint endpoint,
            DateTimeOffset expiresAt,
            long nodeEpoch,
            long generation,
            IReadOnlyDictionary<string, string>? metadata,
            NodeReference? nodeReference,
            MembershipViewId membershipView)
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
            NodeReference = nodeReference;
            MembershipView = membershipView;
        }

        public RouteKey Route { get; }

        public NodeId Node { get; }

        public NodeEndpoint Endpoint { get; }

        public DateTimeOffset ExpiresAt { get; }

        public long NodeEpoch { get; }

        public long Generation { get; }

        public IReadOnlyDictionary<string, string> Metadata { get; }

        public NodeReference? NodeReference { get; }

        public MembershipViewId MembershipView { get; }

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
                && (NodeReference is not null || other.NodeReference is not null
                    ? NodeReference == other.NodeReference
                        && MembershipView == other.MembershipView
                        && Generation == other.Generation
                    : Node == other.Node
                        && NodeEpoch == other.NodeEpoch
                        && Generation == other.Generation);
        }

        public RouteLocation WithExpiresAt(DateTimeOffset expiresAt)
        {
            if (NodeReference is not null)
            {
                return new RouteLocation(
                    Route,
                    Node,
                    Endpoint,
                    expiresAt,
                    NodeEpoch,
                    Generation,
                    Metadata,
                    NodeReference,
                    MembershipView);
            }

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
