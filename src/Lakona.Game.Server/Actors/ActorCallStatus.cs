namespace Lakona.Game.Server.Actors;

/// <summary>
/// Classifies failures from generated actor calls and lower-level actor request/reply calls.
/// </summary>
public enum ActorCallStatus
{
    /// <summary>
    /// The actor route or local actor instance was not found.
    /// </summary>
    ActorNotFound,

    /// <summary>
    /// The target cluster node could not be reached.
    /// </summary>
    NodeUnavailable,

    /// <summary>
    /// The actor call exceeded its queue, dispatch, or response deadline.
    /// </summary>
    Timeout,

    /// <summary>
    /// The actor mailbox or route rejected the call because it was overloaded.
    /// </summary>
    Backpressure,

    /// <summary>
    /// The actor route expired before the call could complete.
    /// </summary>
    Expired,

    /// <summary>
    /// The call failed for a reason that does not map to a more specific status.
    /// </summary>
    Failed
}
