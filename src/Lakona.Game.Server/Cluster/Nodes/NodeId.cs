using System;

namespace Lakona.Game.Cluster
{
    public readonly struct NodeId : IEquatable<NodeId>
    {
        public NodeId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Node id is required.", nameof(value));
            }

            Value = value;
        }

        public string Value { get; }

        public bool Equals(NodeId other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return obj is NodeId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Value);
        }

        public override string ToString()
        {
            return Value;
        }

        public static bool operator ==(NodeId left, NodeId right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(NodeId left, NodeId right)
        {
            return !left.Equals(right);
        }

        public static implicit operator NodeId(string value)
        {
            return new NodeId(value);
        }
    }
}
