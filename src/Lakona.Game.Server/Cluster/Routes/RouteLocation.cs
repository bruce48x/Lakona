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
            NodeReference nodeReference,
            MembershipViewId membershipView,
            NodeEndpoint endpoint,
            IReadOnlyDictionary<string, string>? metadata = null)
        {
            Route = route;
            NodeReference = nodeReference ?? throw new ArgumentNullException(nameof(nodeReference));
            Node = nodeReference.Node;
            Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            Metadata = metadata is null
                ? EmptyMetadata
                : new ReadOnlyDictionary<string, string>(
                    new Dictionary<string, string>(metadata, StringComparer.Ordinal));
            MembershipView = membershipView;
        }

        public RouteKey Route { get; }

        public NodeId Node { get; }

        public NodeEndpoint Endpoint { get; }

        public IReadOnlyDictionary<string, string> Metadata { get; }

        public NodeReference NodeReference { get; }

        public MembershipViewId MembershipView { get; }

        public bool HasSameOwner(RouteLocation other)
        {
            if (other is null)
            {
                throw new ArgumentNullException(nameof(other));
            }

            return Route == other.Route
                && NodeReference == other.NodeReference
                && MembershipView == other.MembershipView;
        }
    }
}
