using Xunit;

namespace Lakona.Game.Server.Tests.Actors;

internal sealed class ManualDeadlineTimeProvider : TimeProvider
{
    private readonly TaskCompletionSource timerScheduled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ManualTimer? timer;

    public Task TimerScheduled => timerScheduled.Task;

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period)
    {
        timer = new ManualTimer(callback, state, dueTime);
        timerScheduled.TrySetResult();
        return timer;
    }

    public void Expire()
    {
        Assert.NotNull(timer);
        timer.Fire();
    }

    private sealed class ManualTimer(
        TimerCallback callback,
        object? state,
        TimeSpan initialDueTime) : ITimer
    {
        private TimeSpan dueTime = initialDueTime;

        public bool Change(TimeSpan newDueTime, TimeSpan period)
        {
            dueTime = newDueTime;
            return true;
        }

        public void Fire()
        {
            if (dueTime != Timeout.InfiniteTimeSpan)
                callback(state);
        }

        public void Dispose()
        {
            dueTime = Timeout.InfiniteTimeSpan;
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return default;
        }
    }
}
