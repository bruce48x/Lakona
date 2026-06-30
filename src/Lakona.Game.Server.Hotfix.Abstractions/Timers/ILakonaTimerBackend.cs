namespace Lakona.Game.Server.Hotfix.Abstractions.Timers;

internal interface ILakonaTimerBackend
{
    ValueTask<TimerId> CreateOnceTimerAsync<TCallback, TArgs>(
        TimeSpan dueTime,
        string methodName,
        TArgs args,
        CancellationToken cancellationToken)
        where TCallback : class;

    ValueTask<TimerId> CreatePeriodicTimerAsync<TCallback, TArgs>(
        TimeSpan dueTime,
        TimeSpan period,
        string methodName,
        TArgs args,
        CancellationToken cancellationToken)
        where TCallback : class;

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
