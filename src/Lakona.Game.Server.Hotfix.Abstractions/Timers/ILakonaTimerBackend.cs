namespace Lakona.Game.Server.Hotfix.Abstractions.Timers;

internal interface ILakonaTimerBackend
{
    ValueTask<TimerId> CreateOnceTimerAsync<TCallback, TArgs>(
        TimeSpan dueTime,
        string methodName,
        TArgs args,
        CancellationToken cancellationToken);

    ValueTask<TimerId> CreatePeriodicTimerAsync<TCallback, TArgs>(
        TimeSpan dueTime,
        TimeSpan period,
        string methodName,
        TArgs args,
        CancellationToken cancellationToken);

    ValueTask DestroyTimerAsync(TimerId timerId, CancellationToken cancellationToken);
}
