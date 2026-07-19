namespace Lakona.Game.Server.Sessions;

/// <summary>
/// Identifies one framework-owned game session.
/// </summary>
/// <remarks>
/// The key is server/framework identity, not a business account model. Use
/// <see cref="OwnerKey"/> for the stable game-owned owner and <see cref="SessionId"/>
/// for the globally unique concrete session instance.
/// </remarks>
public readonly struct GameSessionKey : IEquatable<GameSessionKey>
{
    /// <summary>
    /// Initializes a new game session key.
    /// </summary>
    /// <param name="ownerKey">Stable game-owned owner identity, such as a player id or account id.</param>
    /// <param name="sessionId">Framework session instance id.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="ownerKey"/> or <paramref name="sessionId"/> is empty or whitespace.
    /// </exception>
    public GameSessionKey(string ownerKey, string sessionId)
    {
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            throw new ArgumentException("Owner key is required.", nameof(ownerKey));
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Session id is required.", nameof(sessionId));
        }

        OwnerKey = ownerKey;
        SessionId = sessionId;
    }

    /// <summary>
    /// Gets the stable game-owned owner identity for this session.
    /// </summary>
    public string OwnerKey { get; }

    /// <summary>
    /// Gets the framework session instance id.
    /// </summary>
    public string SessionId { get; }

    /// <summary>
    /// Determines whether this key identifies the same owner and session id.
    /// </summary>
    /// <param name="other">The key to compare with this key.</param>
    /// <returns><see langword="true"/> when all key components match using ordinal string comparison.</returns>
    public bool Equals(GameSessionKey other)
    {
        return string.Equals(OwnerKey, other.OwnerKey, StringComparison.Ordinal)
            && string.Equals(SessionId, other.SessionId, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is GameSessionKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return (StringComparer.Ordinal.GetHashCode(OwnerKey) * 397)
                ^ StringComparer.Ordinal.GetHashCode(SessionId);
        }
    }

    public override string ToString()
    {
        return $"{OwnerKey}/{SessionId}";
    }

    public static bool operator ==(GameSessionKey left, GameSessionKey right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(GameSessionKey left, GameSessionKey right)
    {
        return !left.Equals(right);
    }
}
