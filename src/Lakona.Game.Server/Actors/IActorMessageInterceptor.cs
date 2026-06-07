namespace Lakona.Game.Server.Actors;

public interface IActorMessageInterceptor
{
    ValueTask OnBeforeMessage(
        ActorId actorId,
        string messageType,
        object? message,
        CancellationToken cancellationToken);

    ValueTask OnAfterMessage(
        ActorId actorId,
        string messageType,
        object? message,
        Exception? exception,
        CancellationToken cancellationToken);
}
