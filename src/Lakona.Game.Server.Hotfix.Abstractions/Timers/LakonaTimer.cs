namespace Lakona.Game.Server.Hotfix.Abstractions.Timers;

public static class LakonaTimer
{
    public static ValueTask<TimerId> CreateOnceTimerAsync<TCallback, TArgs>(
        TimeSpan dueTime,
        string methodName,
        TArgs args,
        CancellationToken cancellationToken = default)
        where TCallback : class
    {
        if (dueTime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(dueTime), dueTime, "Due time must not be negative.");
        }

        var context = GetActiveContext();
        return context.Backend.CreateOnceTimerAsync<TCallback, TArgs>(dueTime, methodName, args, cancellationToken);
    }

    public static ValueTask<TimerId> CreatePeriodicTimerAsync<TCallback, TArgs>(
        TimeSpan dueTime,
        TimeSpan period,
        string methodName,
        TArgs args,
        CancellationToken cancellationToken = default)
        where TCallback : class
    {
        if (dueTime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(dueTime), dueTime, "Due time must not be negative.");
        }

        if (period <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(period), period, "Period must be greater than zero.");
        }

        var context = GetActiveContext();
        return context.Backend.CreatePeriodicTimerAsync<TCallback, TArgs>(dueTime, period, methodName, args, cancellationToken);
    }

    public static ValueTask DestroyTimerAsync(TimerId timerId, CancellationToken cancellationToken = default)
    {
        var context = GetActiveContext();
        return context.Backend.DestroyTimerAsync(timerId, cancellationToken);
    }

    private static LakonaTimerExecutionContext GetActiveContext()
    {
        var context = LakonaTimerExecutionScope.Current;
        if (context is null || !context.IsActive)
        {
            throw new InvalidOperationException("Lakona timers can only be used inside an active hotfix execution scope.");
        }

        return context;
    }
}
