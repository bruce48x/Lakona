using System;

namespace Lakona.Game.Cluster
{
    public readonly struct RouteKey : IEquatable<RouteKey>
    {
        public RouteKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Route key is required.", nameof(value));
            }

            Value = value;
        }

        public string Value { get; }

        public bool Equals(RouteKey other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return obj is RouteKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Value);
        }

        public override string ToString()
        {
            return Value;
        }

        public static bool operator ==(RouteKey left, RouteKey right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(RouteKey left, RouteKey right)
        {
            return !left.Equals(right);
        }

        public static implicit operator RouteKey(string value)
        {
            return new RouteKey(value);
        }
    }
}
