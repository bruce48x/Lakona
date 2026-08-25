namespace Lakona.Game.Server.Actors;

internal interface IActorActivationDispatcher
{
    ActorTellResult TryTellExact(
        Type actorType,
        ActorId actorId,
        ActorActivationId activationId,
        Func<IActor, CancellationToken, ValueTask> message,
        CancellationToken cancellationToken = default);

    ValueTask<object?> AskExactAsync(
        Type actorType,
        ActorId actorId,
        ActorActivationId activationId,
        Func<IActor, CancellationToken, ValueTask<object?>> message,
        CancellationToken cancellationToken = default);
}
