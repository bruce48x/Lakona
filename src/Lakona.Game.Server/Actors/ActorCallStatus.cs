namespace Lakona.Game.Server.Actors;

public enum ActorCallStatus
{
    ActorNotFound,
    ActorAlreadyExists,
    ActorOwnershipMismatch,
    NodeUnavailable,
    Timeout,
    Backpressure,
    Expired,
    Failed
}
