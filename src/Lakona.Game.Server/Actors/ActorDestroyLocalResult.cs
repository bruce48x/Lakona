namespace Lakona.Game.Server.Actors;

public sealed record ActorDestroyLocalResult(
    ActorDestroyLocalStatus Status,
    ActorId ActorId,
    Type ActorType,
    string? Diagnostic = null)
{
    public bool Succeeded => Status is ActorDestroyLocalStatus.Destroyed or ActorDestroyLocalStatus.NotFound;
}

public enum ActorDestroyLocalStatus
{
    Destroyed,
    NotFound,
    TypeMismatch,
    TimedOut
}
