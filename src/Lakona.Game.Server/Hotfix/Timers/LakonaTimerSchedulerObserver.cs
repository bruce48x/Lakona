using Lakona.Game.Server.Hotfix.Abstractions.Timers;

namespace Lakona.Game.Server.Hotfix.Timers;

internal interface ILakonaTimerSchedulerObserver
{
    void OnDispatchQueued(LakonaTimerDispatchObservation observation);

    void OnDispatchQueueFull(LakonaTimerDispatchObservation observation);

    void OnDispatchSkipped(LakonaTimerDispatchObservation observation);

    void OnDispatchStarted(LakonaTimerDispatchObservation observation);

    void OnDispatchFailed(LakonaTimerDispatchObservation observation, Exception exception);

    void OnDispatchCompleted(LakonaTimerDispatchObservation observation);

    void OnStaleHeapEntry(LakonaTimerHeapObservation observation);
}

internal readonly record struct LakonaTimerDispatchObservation(
    TimerId TimerId,
    DateTimeOffset DueAtUtc,
    DateTimeOffset ObservedAtUtc,
    TimeSpan? Period,
    long Generation);

internal readonly record struct LakonaTimerHeapObservation(
    TimerId TimerId,
    long Generation);

internal sealed class NullLakonaTimerSchedulerObserver : ILakonaTimerSchedulerObserver
{
    public static NullLakonaTimerSchedulerObserver Instance { get; } = new();

    private NullLakonaTimerSchedulerObserver()
    {
    }

    public void OnDispatchQueued(LakonaTimerDispatchObservation observation)
    {
    }

    public void OnDispatchQueueFull(LakonaTimerDispatchObservation observation)
    {
    }

    public void OnDispatchSkipped(LakonaTimerDispatchObservation observation)
    {
    }

    public void OnDispatchStarted(LakonaTimerDispatchObservation observation)
    {
    }

    public void OnDispatchFailed(LakonaTimerDispatchObservation observation, Exception exception)
    {
    }

    public void OnDispatchCompleted(LakonaTimerDispatchObservation observation)
    {
    }

    public void OnStaleHeapEntry(LakonaTimerHeapObservation observation)
    {
    }
}
