namespace Lakona.Game.Server.Actors;

/// <summary>
/// Identifies one actor in the game actor directory and local runtime.
/// </summary>
/// <remarks>
/// Actor ids are stable business ids such as <c>room/room-123</c> or
/// <c>matchmaking/default</c>. They should not encode connection ids,
/// callback objects, transport endpoints, or temporary node-local state.
/// </remarks>
public readonly record struct ActorId
{
    /// <summary>
    /// Initializes a new actor id.
    /// </summary>
    /// <param name="value">The non-empty actor id string.</param>
    public ActorId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Actor id cannot be empty.", nameof(value));
        }

        Value = value;
    }

    /// <summary>
    /// Gets the actor id string value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Returns the actor id string.
    /// </summary>
    /// <returns>The actor id string value.</returns>
    public override string ToString()
    {
        return Value;
    }

    /// <summary>
    /// Creates an actor id from a non-empty string value.
    /// </summary>
    /// <param name="value">The actor id string.</param>
    /// <returns>The actor id.</returns>
    public static ActorId From(string value)
    {
        return new ActorId(value);
    }
}
