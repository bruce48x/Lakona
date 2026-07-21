using System;

namespace Lakona.Game.Cluster
{
    public readonly struct NodeIncarnationId : IEquatable<NodeIncarnationId>
    {
        public NodeIncarnationId(Guid value)
        {
            if (value == Guid.Empty)
            {
                throw new ArgumentException("Node incarnation id cannot be empty.", nameof(value));
            }

            Value = value;
        }

        public Guid Value { get; }

        public static NodeIncarnationId New()
        {
            return new NodeIncarnationId(Guid.NewGuid());
        }

        public bool Equals(NodeIncarnationId other)
        {
            return Value.Equals(other.Value);
        }

        public override bool Equals(object? obj)
        {
            return obj is NodeIncarnationId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        public override string ToString()
        {
            return Value.ToString("N");
        }

        public static bool operator ==(NodeIncarnationId left, NodeIncarnationId right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(NodeIncarnationId left, NodeIncarnationId right)
        {
            return !left.Equals(right);
        }
    }
}
