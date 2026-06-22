namespace Lakona.Game.Server.Actors;

public sealed record ActorCreateLocalResult(
    ActorCreateLocalStatus Status,
    ActorId ActorId,
    Type ActorType,
    string? Diagnostic = null)
{
    public bool Succeeded =>
        Status is ActorCreateLocalStatus.Created or ActorCreateLocalStatus.AlreadyExistsSameType;
}

public enum ActorCreateLocalStatus
{
    Created,
    AlreadyExistsSameType,
    AlreadyExistsDifferentType
}
