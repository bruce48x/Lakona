namespace Lakona.Game.Server.Actors;

internal interface IActorHostingRuntime
{
    bool TryGetLocalActor(ActorId actorId, out Type actorType, out ActorState state);

    ValueTask<ActorHostingLocalCreateResult> CreateLocalAsync(
        Type actorType,
        ActorId actorId,
        CancellationToken cancellationToken = default);

    ValueTask<ActorHostingLocalDestroyResult> DestroyLocalAsync(
        Type actorType,
        ActorId actorId,
        TimeSpan drainTimeout,
        CancellationToken cancellationToken = default);
}

internal readonly record struct ActorHostingLocalCreateResult(
    ActorHostingLocalCreateStatus Status,
    ActorId ActorId,
    Type RequestedActorType,
    Type? ExistingActorType = null);

internal enum ActorHostingLocalCreateStatus
{
    Created,
    AlreadyExistsSameType,
    AlreadyExistsDifferentType
}

internal readonly record struct ActorHostingLocalDestroyResult(
    ActorHostingLocalDestroyStatus Status,
    ActorId ActorId,
    Type RequestedActorType,
    Type? ExistingActorType = null);

internal enum ActorHostingLocalDestroyStatus
{
    Destroyed,
    NotFound,
    TypeMismatch,
    TimedOut
}
