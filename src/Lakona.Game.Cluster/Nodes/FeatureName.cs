using System;

namespace Lakona.Game.Cluster
{
    public readonly struct FeatureName : IEquatable<FeatureName>
    {
        public FeatureName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Feature name is required.", nameof(value));
            }

            Value = value;
        }

        public string Value { get; }

        public bool Equals(FeatureName other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return obj is FeatureName other && Equals(other);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Value);
        }

        public override string ToString()
        {
            return Value;
        }

        public static bool operator ==(FeatureName left, FeatureName right) => left.Equals(right);

        public static bool operator !=(FeatureName left, FeatureName right) => !left.Equals(right);

        public static implicit operator FeatureName(string value) => new FeatureName(value);
    }
}
