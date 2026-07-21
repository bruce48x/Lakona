using System;

namespace Lakona.Game.Cluster
{
    public sealed class NodeReference : IEquatable<NodeReference>
    {
        public NodeReference(
            ClusterIncarnationId cluster,
            NodeId node,
            NodeIncarnationId incarnation)
        {
            if (cluster.Value == Guid.Empty)
            {
                throw new ArgumentException("Cluster incarnation id is required.", nameof(cluster));
            }

            if (string.IsNullOrWhiteSpace(node.Value))
            {
                throw new ArgumentException("Node id is required.", nameof(node));
            }

            if (incarnation.Value == Guid.Empty)
            {
                throw new ArgumentException("Node incarnation id is required.", nameof(incarnation));
            }

            Cluster = cluster;
            Node = node;
            Incarnation = incarnation;
        }

        public ClusterIncarnationId Cluster { get; }

        public NodeId Node { get; }

        public NodeIncarnationId Incarnation { get; }

        public bool Equals(NodeReference? other)
        {
            return other is not null
                && Cluster == other.Cluster
                && Node == other.Node
                && Incarnation == other.Incarnation;
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as NodeReference);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Cluster, Node, Incarnation);
        }

        public override string ToString()
        {
            return Node.Value + "@" + Incarnation;
        }

        public static bool operator ==(NodeReference? left, NodeReference? right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(NodeReference? left, NodeReference? right)
        {
            return !Equals(left, right);
        }
    }
}
