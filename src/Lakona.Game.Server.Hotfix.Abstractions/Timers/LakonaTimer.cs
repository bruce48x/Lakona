using Lakona.Game.Server.Hotfix.Abstractions;

namespace Lakona.Game.Server.Hotfix.Abstractions.Timers;

/// <summary>
/// Creates and destroys framework-owned hotfix timers.
/// </summary>
/// <remarks>
/// Timers must be created inside an active hotfix execution scope, usually from
/// a hotfix feature <c>StartAsync</c> method. Store the returned <see cref="TimerId"/>
/// in <see cref="HotfixFeatureState"/> when the timer should be destroyed later.
/// Timer callbacks are resolved by type and method name so hotfix reload can call
/// the same method name on the newest loaded hotfix assembly.
/// </remarks>
public static class LakonaTimer
{
    /// <summary>
    /// Creates a timer that fires once.
    /// </summary>
    /// <typeparam name="TCallback">The callback type that declares the static callback method.</typeparam>
    /// <typeparam name="TArgs">The serializable argument type passed to the callback.</typeparam>
    /// <param name="dueTime">The delay before the timer first fires.</param>
    /// <param name="methodName">The callback method name. Use <c>nameof(...)</c> rather than a string literal.</param>
    /// <param name="args">The callback arguments.</param>
    /// <param name="cancellationToken">A token that cancels timer creation.</param>
    /// <returns>The framework-assigned timer id.</returns>
    /// <remarks>
    /// The callback method must be a public static method on
    /// <typeparamref name="TCallback"/> that accepts <see cref="TimerTick{TArgs}"/>.
    /// </remarks>
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

    /// <summary>
    /// Creates a timer that fires repeatedly until it is destroyed.
    /// </summary>
    /// <typeparam name="TCallback">The callback type that declares the static callback method.</typeparam>
    /// <typeparam name="TArgs">The serializable argument type passed to each callback invocation.</typeparam>
    /// <param name="dueTime">The delay before the timer first fires.</param>
    /// <param name="period">The interval between callback attempts.</param>
    /// <param name="methodName">The callback method name. Use <c>nameof(...)</c> rather than a string literal.</param>
    /// <param name="args">The callback arguments.</param>
    /// <param name="cancellationToken">A token that cancels timer creation.</param>
    /// <returns>The framework-assigned timer id.</returns>
    /// <remarks>
    /// If the hotfix assembly reloads while the timer exists, the next tick resolves
    /// <paramref name="methodName"/> on the newest loaded <typeparamref name="TCallback"/>
    /// type. Missing callback methods are reported and skipped.
    /// </remarks>
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

    /// <summary>
    /// Destroys an existing timer.
    /// </summary>
    /// <param name="timerId">The timer id returned by a create method.</param>
    /// <param name="cancellationToken">A token that cancels timer destruction.</param>
    /// <returns>A task-like value that completes when the timer is removed.</returns>
    /// <remarks>
    /// Feature shutdown code should usually pass <see cref="CancellationToken.None"/>
    /// so cleanup still runs when the stop request token has already been canceled.
    /// </remarks>
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
