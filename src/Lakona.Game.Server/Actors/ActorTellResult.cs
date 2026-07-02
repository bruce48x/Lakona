namespace Lakona.Game.Server.Actors;

/// <summary>
/// Reports whether a fire-and-forget actor message was accepted for local dispatch.
/// </summary>
public enum ActorTellResult
{
    /// <summary>
    /// The message was accepted by the actor mailbox.
    /// </summary>
    Accepted = 0,

    /// <summary>
    /// The actor mailbox was full.
    /// </summary>
    MailboxFull = 1,

    /// <summary>
    /// The actor exists but is not currently able to accept messages.
    /// </summary>
    ActorUnavailable = 2,

    /// <summary>
    /// No local actor with the requested id was found.
    /// </summary>
    ActorNotFound = 3
}
