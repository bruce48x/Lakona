using System.Globalization;

namespace Lakona.Game.Server.Hotfix.Abstractions.Timers;

/// <summary>
/// Identifies a framework-owned hotfix timer.
/// </summary>
/// <remarks>
/// Timer ids are assigned by the framework when a timer is created. User code
/// should store the returned id when it needs to destroy the timer later; user
/// code should not manufacture ids manually.
/// </remarks>
public readonly struct TimerId : IEquatable<TimerId>
{
    private readonly Guid value;

    private TimerId(Guid value)
    {
        this.value = value;
    }

    /// <summary>
    /// Gets a value indicating whether this id refers to a framework-assigned timer id.
    /// </summary>
    public bool IsValid => value != Guid.Empty;

    internal static TimerId FromGuid(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Timer id must not be empty.", nameof(value));
        }

        return new TimerId(value);
    }

    /// <inheritdoc />
    public bool Equals(TimerId other)
    {
        return value.Equals(other.value);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is TimerId other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return value.GetHashCode();
    }

    /// <summary>
    /// Returns the timer id string.
    /// </summary>
    /// <returns>The timer id string, or <c>invalid</c> for the default value.</returns>
    public override string ToString()
    {
        return IsValid ? value.ToString("D", CultureInfo.InvariantCulture) : "invalid";
    }

    /// <summary>
    /// Compares two timer ids for equality.
    /// </summary>
    /// <param name="left">The left timer id.</param>
    /// <param name="right">The right timer id.</param>
    /// <returns><see langword="true"/> when the ids are equal; otherwise, <see langword="false"/>.</returns>
    public static bool operator ==(TimerId left, TimerId right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Compares two timer ids for inequality.
    /// </summary>
    /// <param name="left">The left timer id.</param>
    /// <param name="right">The right timer id.</param>
    /// <returns><see langword="true"/> when the ids differ; otherwise, <see langword="false"/>.</returns>
    public static bool operator !=(TimerId left, TimerId right)
    {
        return !left.Equals(right);
    }
}
