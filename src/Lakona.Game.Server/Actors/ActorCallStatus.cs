namespace Lakona.Game.Server.Actors;

public enum ActorCallStatus
{
    ActorNotFound,
    NodeUnavailable,
    Timeout,
    Backpressure,
    Expired,
    Failed
}
