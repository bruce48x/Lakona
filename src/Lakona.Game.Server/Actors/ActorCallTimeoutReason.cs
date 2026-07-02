namespace Lakona.Game.Server.Actors;

/// <summary>
/// Identifies which actor call deadline was exceeded.
/// </summary>
public enum ActorCallTimeoutReason
{
    /// <summary>
    /// The actor accepted the call but did not produce a reply before the response deadline.
    /// </summary>
    ResponseTimeout = 0,

    /// <summary>
    /// The call waited too long before entering the actor mailbox.
    /// </summary>
    QueueTimeout = 1
}
