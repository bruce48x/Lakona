using System;

namespace Lakona.Game.Cluster
{
    public readonly struct ClusterIncarnationId : IEquatable<ClusterIncarnationId>
    {
        public ClusterIncarnationId(Guid value)
        {
            if (value == Guid.Empty)
            {
                throw new ArgumentException("Cluster incarnation id cannot be empty.", nameof(value));
            }

            Value = value;
        }

        public Guid Value { get; }

        public static ClusterIncarnationId New()
        {
            return new ClusterIncarnationId(Guid.NewGuid());
        }

        public bool Equals(ClusterIncarnationId other)
        {
            return Value.Equals(other.Value);
        }

        public override bool Equals(object? obj)
        {
            return obj is ClusterIncarnationId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        public override string ToString()
        {
            return Value.ToString("N");
        }

        public static bool operator ==(ClusterIncarnationId left, ClusterIncarnationId right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(ClusterIncarnationId left, ClusterIncarnationId right)
        {
            return !left.Equals(right);
        }
    }
}
