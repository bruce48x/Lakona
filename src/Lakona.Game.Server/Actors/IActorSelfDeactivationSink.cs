namespace Lakona.Game.Server.Actors;

internal interface IActorSelfDeactivationSink
{
    ValueTask DeactivateAsync(
        Type actorType,
        ActorId actorId,
        object actor,
        CancellationToken cancellationToken = default);
}
