using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Lakona.Game.Server.Hotfix.Timers;

/// <summary>A timer callback executed inside its owning Actor's mailbox.</summary>
public delegate ValueTask ActorTimerCallback<TActor, TArgs>(TActor actor, TimerTick<TArgs> tick)
    where TActor : Actor;

/// <summary>Creates and cancels timers owned by the current exact Actor activation.</summary>
public static class ActorTimer
{
    /// <summary>Schedules one callback. Capacity pressure delays delivery instead of discarding it.</summary>
    public static TimerId CreateOnceTimer<TActor, TBehavior, TArgs>(
        this TActor actor,
        [HotfixMethodSelector] Func<TBehavior, ActorTimerCallback<TActor, TArgs>> selector,
        TimeSpan dueTime,
        TArgs args)
        where TActor : Actor where TBehavior : class =>
        Create(actor, selector, dueTime, null, args);

    /// <summary>
    /// Schedules one callback at a time. After actual execution completes, the next due time
    /// is the later of that completion and the previous execution's start plus period.
    /// Stopping the owning activation automatically cancels its timers.
    /// </summary>
    public static TimerId CreatePeriodicTimer<TActor, TBehavior, TArgs>(
        this TActor actor,
        [HotfixMethodSelector] Func<TBehavior, ActorTimerCallback<TActor, TArgs>> selector,
        TimeSpan dueTime,
        TimeSpan period,
        TArgs args)
        where TActor : Actor where TBehavior : class
    {
        if (period <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(period));
        return Create(actor, selector, dueTime, period, args);
    }

    /// <summary>
    /// Cancels a timer owned by this exact activation, without waiting for its callback.
    /// Missing or completed timers are ignored. A live timer belonging to another
    /// activation is rejected. Must be called in the owner's active Hotfix turn.
    /// </summary>
    public static void DestroyTimer(this Actor actor, TimerId timerId)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var owner = actor.Context.TimerOwner
            ?? throw new InvalidOperationException("Actor timers require a hosted Actor activation.");
        owner.ValidateTurn();
        var context = LakonaTimerExecutionScope.GetActiveContext();
        context.Backend.DestroyTimer(actor, timerId);
    }

    private static TimerId Create<TActor, TBehavior, TArgs>(
        TActor actor, Func<TBehavior, ActorTimerCallback<TActor, TArgs>> selector,
        TimeSpan dueTime, TimeSpan? period, TArgs args)
        where TActor : Actor where TBehavior : class
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(selector);
        var owner = actor.Context?.TimerOwner
            ?? throw new InvalidOperationException("Actor timers require a hosted Actor activation.");
        owner.ValidateCreation();
        var context = LakonaTimerExecutionScope.GetActiveContext();
        return context.Backend.CreateTimer(actor, selector, dueTime, period, args);
    }
}
