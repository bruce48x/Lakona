using Lakona.Game.Server.Hotfix.Abstractions;

namespace Lakona.Game.Server.Hotfix.Abstractions.Timers;

/// <summary>
/// Creates and destroys framework-owned hotfix timers.
/// </summary>
/// <remarks>
/// Timers must be created inside an active hotfix execution scope. Store the
/// returned <see cref="TimerId"/> in stable state when the timer should be
/// destroyed later.
/// Timer callbacks are resolved by type and method name so hotfix reload can call
/// the same method name on the newest loaded hotfix assembly.
/// </remarks>
public static class LakonaTimer
{
    public static ValueTask<TimerId> CreateOnceTimerAsync<TCallback, TArgs>(
        [HotfixMethodSelector] Func<TCallback, HotfixTimerCallback<TArgs>> callbackSelector,
        TimeSpan dueTime,
        TArgs args,
        CancellationToken cancellationToken = default)
        where TCallback : class
    {
        ArgumentNullException.ThrowIfNull(callbackSelector);
        var context = GetActiveContext();
        var callback = ResolveTimerEntry(context, callbackSelector);
        return CreateOnceTimerAsync(callback, dueTime, args, cancellationToken);
    }

    /// <summary>
    /// Creates a timer that fires once.
    /// </summary>
    /// <typeparam name="TArgs">The serializable argument type passed to the callback.</typeparam>
    /// <param name="callback">The generated callback entry.</param>
    /// <param name="dueTime">The delay before the timer first fires.</param>
    /// <param name="args">The callback arguments.</param>
    /// <param name="cancellationToken">A token that cancels timer creation.</param>
    /// <returns>The framework-assigned timer id.</returns>
    /// <remarks>
    /// The callback entry is generated from a public instance method on a
    /// <see cref="HotfixTimerAttribute"/> module.
    /// </remarks>
    public static ValueTask<TimerId> CreateOnceTimerAsync<TArgs>(
        HotfixTimerEntry<TArgs> callback,
        TimeSpan dueTime,
        TArgs args,
        CancellationToken cancellationToken = default)
    {
        if (dueTime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(dueTime), dueTime, "Due time must not be negative.");
        }

        var context = GetActiveContext();
        return context.Backend.CreateOnceTimerAsync(callback, dueTime, args, cancellationToken);
    }

    /// <summary>
    /// Creates a timer that fires repeatedly until it is destroyed.
    /// </summary>
    /// <typeparam name="TArgs">The serializable argument type passed to each callback invocation.</typeparam>
    /// <param name="callback">The generated callback entry.</param>
    /// <param name="dueTime">The delay before the timer first fires.</param>
    /// <param name="period">The interval between callback attempts.</param>
    /// <param name="args">The callback arguments.</param>
    /// <param name="cancellationToken">A token that cancels timer creation.</param>
    /// <returns>The framework-assigned timer id.</returns>
    /// <remarks>
    /// If the hotfix assembly reloads while the timer exists, the next tick resolves
    /// the generated entry on the newest loaded hotfix generation. Missing
    /// callback entries are reported and skipped.
    /// </remarks>
    public static ValueTask<TimerId> CreatePeriodicTimerAsync<TArgs>(
        HotfixTimerEntry<TArgs> callback,
        TimeSpan dueTime,
        TimeSpan period,
        TArgs args,
        CancellationToken cancellationToken = default)
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
        return context.Backend.CreatePeriodicTimerAsync(callback, dueTime, period, args, cancellationToken);
    }

    public static ValueTask<TimerId> CreatePeriodicTimerAsync<TCallback, TArgs>(
        [HotfixMethodSelector] Func<TCallback, HotfixTimerCallback<TArgs>> callbackSelector,
        TimeSpan dueTime,
        TimeSpan period,
        TArgs args,
        CancellationToken cancellationToken = default)
        where TCallback : class
    {
        ArgumentNullException.ThrowIfNull(callbackSelector);
        var context = GetActiveContext();
        var callback = ResolveTimerEntry(context, callbackSelector);
        return CreatePeriodicTimerAsync(callback, dueTime, period, args, cancellationToken);
    }

    /// <summary>
    /// Destroys an existing timer.
    /// </summary>
    /// <param name="timerId">The timer id returned by a create method.</param>
    /// <param name="cancellationToken">A token that cancels timer destruction.</param>
    /// <returns>A task-like value that completes when the timer is removed.</returns>
    /// <remarks>
    /// Shutdown code should usually pass <see cref="CancellationToken.None"/>
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

    private static HotfixTimerEntry<TArgs> ResolveTimerEntry<TCallback, TArgs>(
        LakonaTimerExecutionContext context,
        Func<TCallback, HotfixTimerCallback<TArgs>> callbackSelector)
        where TCallback : class
    {
        if (context.RuntimeContext is not IHotfixTimerEntryResolver resolver)
        {
            throw new InvalidOperationException("The active hotfix runtime does not support typed timer callback selectors.");
        }

        return resolver.ResolveTimerEntry(callbackSelector);
    }
}
