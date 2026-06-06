namespace Lakona.Actor.Core;

internal sealed class ActorStopper
{
    private readonly ActorRegistry registry;

    internal ActorStopper(ActorRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        this.registry = registry;
    }

    internal async ValueTask StopAsync(ActorId target)
    {
        if (!registry.TryGet(target, out ActorCell? cell))
        {
            return;
        }

        try
        {
            await cell.RequestStopAsync().ConfigureAwait(false);
        }
        finally
        {
            RemoveActor(target, cell);
        }
    }

    internal async ValueTask<ActorStopResult> StopAsync(ActorId target, TimeSpan drainTimeout)
    {
        if (drainTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(drainTimeout), "Drain timeout must be greater than zero.");
        }

        if (!registry.TryGet(target, out ActorCell? cell))
        {
            return ActorStopResult.Drained;
        }

        ActorStopResult result;
        Task stopTask = cell.RequestStopAsync();

        try
        {
            await stopTask.WaitAsync(drainTimeout).ConfigureAwait(false);
            result = ActorStopResult.Drained;
        }
        catch (TimeoutException)
        {
            result = ActorStopResult.TimedOut;
        }
        catch
        {
            RemoveActor(target, cell);
            throw;
        }

        if (result == ActorStopResult.Drained)
        {
            RemoveActor(target, cell);
        }
        else
        {
            _ = RemoveActorWhenCompleted(target, cell);
        }

        return result;
    }

    internal async ValueTask StopAllForDisposeAsync()
    {
        ActorCell[] cells = registry.SnapshotAndClear();

        foreach (ActorCell cell in cells)
        {
            await cell.StopAsync().ConfigureAwait(false);
        }
    }

    private async Task RemoveActorWhenCompleted(ActorId target, ActorCell cell)
    {
        try
        {
            await cell.Completion.ConfigureAwait(false);
        }
        finally
        {
            RemoveActor(target, cell);
        }
    }

    private void RemoveActor(ActorId target, ActorCell cell)
    {
        registry.Remove(target, cell);
    }
}
