namespace Lakona.Game.Server.Actors;

public interface IActorPlacementService
{
    ValueTask<ActorPlacementResult> PlaceAsync<TActor, TKey>(
        TKey key,
        ActorPlacementCreateMode createMode,
        CancellationToken cancellationToken = default)
        where TActor : class, IActor
        where TKey : notnull;

    ValueTask<ActorPlacementResult> PlaceAsync<TActor, TKey>(
        ActorId actorId,
        TKey key,
        ActorPlacementCreateMode createMode,
        CancellationToken cancellationToken = default)
        where TActor : class, IActor
        where TKey : notnull =>
        PlaceAsync<TActor, TKey>(key, createMode, cancellationToken);

    ValueTask<ActorPlacementResult> PlaceAsync<TActor, TKey>(
        ActorId actorId, TKey key, ActorPlacementCreateMode createMode,
        ActorStartArguments arguments, CancellationToken cancellationToken = default)
        where TActor : class, IActor
        where TKey : notnull => throw new NotSupportedException("This placement provider does not support Actor start arguments.");

    ValueTask DestroyAsync<TActor>(
        ActorId actorId,
        CancellationToken cancellationToken = default)
        where TActor : class, IActor;
}
