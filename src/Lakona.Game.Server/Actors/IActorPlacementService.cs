namespace Lakona.Game.Server.Actors;

public interface IActorPlacementService
{
    ValueTask<ActorPlacementResult> PlaceAsync<TActor, TKey>(
        TKey key,
        ActorPlacementCreateMode createMode,
        CancellationToken cancellationToken = default)
        where TActor : class, IActor;
}
