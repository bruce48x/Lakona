using System;

namespace Lakona.Game.Cluster
{
    public readonly struct ClusterFeature : IEquatable<ClusterFeature>
    {
        public ClusterFeature(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Cluster feature is required.", nameof(value));
            }

            Value = value;
        }

        public string Value { get; }

        public bool Equals(ClusterFeature other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return obj is ClusterFeature other && Equals(other);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Value);
        }

        public override string ToString()
        {
            return Value;
        }

        public static bool operator ==(ClusterFeature left, ClusterFeature right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(ClusterFeature left, ClusterFeature right)
        {
            return !left.Equals(right);
        }

        public static implicit operator ClusterFeature(string value)
        {
            return new ClusterFeature(value);
        }
    }
}
