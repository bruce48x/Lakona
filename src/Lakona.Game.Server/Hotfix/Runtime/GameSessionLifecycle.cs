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
    /// <param name="call">The disconnected session, connection and generation-pinned services.</param>
    ValueTask SessionDisconnectedAsync(HotfixLifecycleCall<GameSessionDisconnectedRequest> call);

    /// <summary>
    /// Called after background cleanup removes an expired disconnected session.
    /// That session can no longer resume; release its remaining business resources.
    /// </summary>
    /// <remarks>
    /// This is not a general termination callback: explicitly terminated sessions
    /// do not produce this event. A newer session may already exist for the owner.
    /// </remarks>
    /// <param name="call">The expired session, its last connection and generation-pinned services.</param>
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
