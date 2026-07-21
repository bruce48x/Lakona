using System;

namespace Lakona.Game.Cluster
{
    public readonly struct MembershipViewId : IEquatable<MembershipViewId>, IComparable<MembershipViewId>
    {
        public MembershipViewId(long value)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Membership view id cannot be negative.");
            }

            Value = value;
        }

        public long Value { get; }

        public int CompareTo(MembershipViewId other)
        {
            return Value.CompareTo(other.Value);
        }

        public bool Equals(MembershipViewId other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object? obj)
        {
            return obj is MembershipViewId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        public override string ToString()
        {
            return Value.ToString();
        }

        public static bool operator ==(MembershipViewId left, MembershipViewId right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(MembershipViewId left, MembershipViewId right)
        {
            return !left.Equals(right);
        }
    }
}
