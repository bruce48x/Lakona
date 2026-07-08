namespace Lakona.Game.Server.Actors;

public interface IActorLifecycleDispatcher
{
    ValueTask StartAsync(
        Type actorType,
        ActorId actorId,
        object actor,
        CancellationToken cancellationToken = default);

    ValueTask StopAsync(
        Type actorType,
        ActorId actorId,
        object actor,
        CancellationToken cancellationToken = default);
}

internal sealed class NoopActorLifecycleDispatcher : IActorLifecycleDispatcher
{
    public ValueTask StartAsync(
        Type actorType,
        ActorId actorId,
        object actor,
        CancellationToken cancellationToken = default)
    {
        return default;
    }

    public ValueTask StopAsync(
        Type actorType,
        ActorId actorId,
        object actor,
        CancellationToken cancellationToken = default)
    {
        return default;
    }
}
