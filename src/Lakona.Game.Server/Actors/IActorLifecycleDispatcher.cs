namespace Lakona.Game.Server.Actors;

public interface IActorLifecycleDispatcher
{
    bool HasStartHook(Type actorType);

    bool HasStopHook(Type actorType);

    ValueTask StartAsync(
        Type actorType,
        ActorId actorId,
        object actor,
        CancellationToken cancellationToken = default);

    ValueTask StartAsync(Type actorType, ActorId actorId, object actor,
        ActorStartArguments arguments, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This lifecycle dispatcher does not support Actor start arguments.");

    ValueTask StopAsync(
        Type actorType,
        ActorId actorId,
        object actor,
        CancellationToken cancellationToken = default);
}

internal sealed class NoopActorLifecycleDispatcher : IActorLifecycleDispatcher
{
    public bool HasStartHook(Type actorType)
    {
        return false;
    }

    public bool HasStopHook(Type actorType)
    {
        return false;
    }

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
