namespace Lakona.Game.Server.Actors;

/// <summary>
/// Describes the lifecycle state of a local actor instance.
/// </summary>
public enum ActorState
{
    /// <summary>
    /// The actor is active and can accept messages.
    /// </summary>
    Active,

    /// <summary>
    /// The actor is stopping and draining accepted messages.
    /// </summary>
    Draining,

    /// <summary>
    /// The actor has stopped and is no longer callable.
    /// </summary>
    Dead
}
