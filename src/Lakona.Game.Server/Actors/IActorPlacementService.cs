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

    ValueTask DestroyAsync<TActor>(
        ActorId actorId,
        CancellationToken cancellationToken = default)
        where TActor : class, IActor;
}
