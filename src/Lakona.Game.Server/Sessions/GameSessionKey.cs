namespace Lakona.Game.Server.Sessions;

/// <summary>
/// Identifies one framework-owned game session.
/// </summary>
/// <remarks>
/// The key is server/framework identity, not a business account model. Use
/// <see cref="OwnerKey"/> for the stable game-owned owner, <see cref="SessionId"/>
/// for the concrete session instance, and <see cref="Generation"/> to distinguish
/// replacement sessions that reuse the same owner or session id.
/// </remarks>
public readonly struct GameSessionKey : IEquatable<GameSessionKey>
{
    /// <summary>
    /// Initializes a new game session key.
    /// </summary>
    /// <param name="ownerKey">Stable game-owned owner identity, such as a player id or account id.</param>
    /// <param name="sessionId">Framework session instance id.</param>
    /// <param name="generation">Positive session generation used to distinguish replacement sessions.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="ownerKey"/> or <paramref name="sessionId"/> is empty or whitespace.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="generation"/> is zero or negative.
    /// </exception>
    public GameSessionKey(string ownerKey, string sessionId, long generation)
    {
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            throw new ArgumentException("Owner key is required.", nameof(ownerKey));
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Session id is required.", nameof(sessionId));
        }

        if (generation <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(generation), "Session generation must be positive.");
        }

        OwnerKey = ownerKey;
        SessionId = sessionId;
        Generation = generation;
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
    /// Gets the positive generation for this session.
    /// </summary>
    public long Generation { get; }

    /// <summary>
    /// Determines whether this key identifies the same owner, session id, and generation.
    /// </summary>
    /// <param name="other">The key to compare with this key.</param>
    /// <returns><see langword="true"/> when all key components match using ordinal string comparison.</returns>
    public bool Equals(GameSessionKey other)
    {
        return string.Equals(OwnerKey, other.OwnerKey, StringComparison.Ordinal)
            && string.Equals(SessionId, other.SessionId, StringComparison.Ordinal)
            && Generation == other.Generation;
    }

    public override bool Equals(object? obj)
    {
        return obj is GameSessionKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = StringComparer.Ordinal.GetHashCode(OwnerKey);
            hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(SessionId);
            hash = (hash * 397) ^ Generation.GetHashCode();
            return hash;
        }
    }

    public override string ToString()
    {
        return $"{OwnerKey}/{SessionId}/{Generation}";
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
