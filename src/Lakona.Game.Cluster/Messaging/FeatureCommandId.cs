using System;

namespace Lakona.Game.Cluster;

public readonly struct FeatureCommandId : IEquatable<FeatureCommandId>
{
    public FeatureCommandId(int value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Feature command id must be positive.");
        }

        Value = value;
    }

    public int Value { get; }

    public static FeatureCommandId From(int value)
    {
        return new FeatureCommandId(value);
    }

    public override string ToString()
    {
        return Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public bool Equals(FeatureCommandId other)
    {
        return Value == other.Value;
    }

    public override bool Equals(object? obj)
    {
        return obj is FeatureCommandId other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Value;
    }

    public static bool operator ==(FeatureCommandId left, FeatureCommandId right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(FeatureCommandId left, FeatureCommandId right)
    {
        return !left.Equals(right);
    }
}
