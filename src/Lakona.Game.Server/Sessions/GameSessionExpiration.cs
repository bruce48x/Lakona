namespace Lakona.Game.Server.Sessions;

/// <summary>
/// Identifies why a Game Session record reached its exact recovery deadline.
/// </summary>
public enum GameSessionExpirationKind
{
    /// <summary>
    /// The Session remained disconnected until its resume deadline.
    /// </summary>
    Disconnected = 0,

    /// <summary>
    /// The Session retained a terminal recovery outcome until its deadline.
    /// </summary>
    RetainedTermination = 1,
}

/// <summary>
/// Describes a Game Session record removed after its recovery deadline.
/// </summary>
/// <param name="Session">The expired Game Session.</param>
/// <param name="ConnectionId">The last framework connection id, when available.</param>
/// <param name="Kind">The reason the record expired.</param>
public sealed record GameSessionExpiration(
    GameSessionKey Session,
    string? ConnectionId,
    GameSessionExpirationKind Kind);
