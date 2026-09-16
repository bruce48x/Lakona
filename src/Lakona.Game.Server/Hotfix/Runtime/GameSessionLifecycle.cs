namespace Lakona.Game.Server.Hotfix;

/// <summary>Replaceable business callbacks for individual game sessions.</summary>
/// <remarks>
/// Implement this interface on a class marked with [HotfixLifecycle]. Events
/// identify a particular session, not an owner's aggregate online status.
/// Check the event's session identity before changing current business state.
/// </remarks>
public interface IGameSessionLifecycle
{
    /// <summary>
    /// Called after a bound connection disconnects and its game session is marked
    /// disconnected. The session may still resume within its recovery window.
    /// </summary>
    /// <remarks>Use for temporary presence changes; defer final cleanup until expiration.</remarks>
    /// <param name="call">The disconnected session and connection event data.</param>
    ValueTask SessionDisconnectedAsync(HotfixLifecycleCall<GameSessionDisconnectedRequest> call);

    /// <summary>Called after a recovered session's heartbeat completes the reliable replay step.</summary>
    /// <remarks>
    /// Independent of reliable push. Initial login and failed recovery do not trigger this callback.
    /// Check the session identity before restoring business presence. Replay completion does not
    /// mean the client has acknowledged every message.
    /// </remarks>
    ValueTask SessionResumedAsync(HotfixLifecycleCall<GameSessionResumedRequest> call);

    /// <summary>
    /// Called after background cleanup removes an expired disconnected session.
    /// That session can no longer resume; release its remaining business resources.
    /// </summary>
    /// <remarks>
    /// This is not a general termination callback: explicitly terminated sessions
    /// do not produce this event. A newer session may already exist for the owner.
    /// </remarks>
    /// <param name="call">The expired session and its last connection event data.</param>
    ValueTask SessionExpiredAsync(HotfixLifecycleCall<GameSessionExpiredRequest> call);
}

public sealed class GameSessionDisconnectedRequest
{
    public string OwnerKey { get; set; } = "";

    public string SessionId { get; set; } = "";

    public string ConnectionId { get; set; } = "";

}

public sealed class GameSessionExpiredRequest
{
    public string OwnerKey { get; set; } = "";

    public string SessionId { get; set; } = "";

    public string ConnectionId { get; set; } = "";

}

/// <summary>The existing session and its replacement connection after recovery.</summary>
public sealed class GameSessionResumedRequest
{
    public string OwnerKey { get; set; } = "";

    public string SessionId { get; set; } = "";

    public string ConnectionId { get; set; } = "";
}
