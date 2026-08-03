using System.ComponentModel;

namespace Lakona.Game.Server.Hotfix.Abstractions.Timers;

[EditorBrowsable(EditorBrowsableState.Never)]
public interface ILakonaTimerBackend
{
    ValueTask<TimerId> CreateOnceTimerAsync<TArgs>(
        IHotfixTimerEntryResolver runtimeContext,
        HotfixTimerEntry<TArgs> callback,
        TimeSpan dueTime,
        TArgs args,
        CancellationToken cancellationToken);

    ValueTask<TimerId> CreatePeriodicTimerAsync<TArgs>(
        IHotfixTimerEntryResolver runtimeContext,
        HotfixTimerEntry<TArgs> callback,
        TimeSpan dueTime,
        TimeSpan period,
        TArgs args,
        CancellationToken cancellationToken);

    ValueTask DestroyTimerAsync(TimerId timerId, CancellationToken cancellationToken);

    ILakonaTimerBackend CreateStagingBackend()
    {
        return this;
    }

    ValueTask CommitStagedTimersAsync(ILakonaTimerBackend stagingBackend, CancellationToken cancellationToken)
    {
        return default;
    }

    ValueTask RollbackStagedTimersAsync(ILakonaTimerBackend stagingBackend, CancellationToken cancellationToken)
    {
        return default;
    }
}
