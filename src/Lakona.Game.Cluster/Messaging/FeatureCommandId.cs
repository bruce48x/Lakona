using System;
using System.Globalization;

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

    public static bool TryParse(string? value, out FeatureCommandId commandId)
    {
        commandId = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        if (parsed <= 0)
        {
            return false;
        }

        commandId = new FeatureCommandId(parsed);
        return true;
    }

    public override string ToString()
    {
        return Value.ToString(CultureInfo.InvariantCulture);
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
