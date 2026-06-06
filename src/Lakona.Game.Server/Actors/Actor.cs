namespace Lakona.Game.Server.Actors;

public abstract class Actor : IActor
{
    public ActorContext Context { get; private set; } = ActorContext.Uninitialized;

    internal async ValueTask ActivateAsync(ActorContext context, CancellationToken cancellationToken)
    {
        Context = context;
        await OnActivateAsync(cancellationToken).ConfigureAwait(false);
    }

    internal async ValueTask DeactivateAsync(CancellationToken cancellationToken)
    {
        await OnDeactivateAsync(cancellationToken).ConfigureAwait(false);
    }

    protected virtual ValueTask OnActivateAsync(CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }

    protected virtual ValueTask OnDeactivateAsync(CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}

public abstract class Actor<TKey> : Actor
    where TKey : notnull
{
}
