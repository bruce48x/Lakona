using System.Globalization;

namespace Lakona.Game.Server.Hotfix.Abstractions.Timers;

public readonly struct TimerId : IEquatable<TimerId>
{
    private readonly Guid value;

    private TimerId(Guid value)
    {
        this.value = value;
    }

    public bool IsValid => value != Guid.Empty;

    internal static TimerId FromGuid(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Timer id must not be empty.", nameof(value));
        }

        return new TimerId(value);
    }

    public bool Equals(TimerId other)
    {
        return value.Equals(other.value);
    }

    public override bool Equals(object? obj)
    {
        return obj is TimerId other && Equals(other);
    }

    public override int GetHashCode()
    {
        return value.GetHashCode();
    }

    public override string ToString()
    {
        return IsValid ? value.ToString("D", CultureInfo.InvariantCulture) : "invalid";
    }

    public static bool operator ==(TimerId left, TimerId right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(TimerId left, TimerId right)
    {
        return !left.Equals(right);
    }
}
