namespace Lakona.Game.Server.Actors;

public interface IActorLifecycle
{
    ValueTask<ActorCreateLocalResult> CreateLocalAsync(
        Type actorType,
        ActorId actorId,
        ActorCreateOptions? options = null,
        CancellationToken cancellationToken = default);

    ValueTask<ActorCreateLocalResult> CreateLocalAsync<TActor>(
        ActorId actorId,
        ActorCreateOptions? options = null,
        CancellationToken cancellationToken = default)
        where TActor : class, IActor;

    ValueTask<ActorDestroyLocalResult> DestroyLocalAsync<TActor>(
        ActorId actorId,
        ActorDestroyOptions? options = null,
        CancellationToken cancellationToken = default)
        where TActor : class, IActor;
}
