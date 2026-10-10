using Xunit;

namespace Lakona.Game.Server.Hosting;

public sealed class MembershipTableSafetyWindowTests
{
    [Fact]
    public async Task Expiry_is_terminal_and_does_not_wait_for_admitted_work()
    {
        var time = new MembershipTestTimeProvider();
        var gate = new DistributedWorkAdmissionGate(time);
        var stopped = 0;
        using var window = new MembershipTableSafetyWindow(time, TimeSpan.FromSeconds(2), gate, () => stopped++);
        gate.SafetyWindow = window;
        window.Start();
        gate.Open();
        Assert.True(gate.TryEnter(out var work));
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(1, stopped);
        Assert.False(gate.TryEnter(out _));
        Assert.False(window.Renew());
        Assert.Throws<InvalidOperationException>(gate.Open);
        var drain = gate.CloseAndDrainAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken).AsTask();
        Assert.False(drain.IsCompleted);
        gate.Exit(work);
        Assert.True(await drain);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Delayed_timer_cannot_allow_admission_or_late_renewal(bool renewFirst)
    {
        var time = new MembershipTestTimeProvider();
        var gate = new DistributedWorkAdmissionGate(time);
        var stopped = 0;
        using var window = new MembershipTableSafetyWindow(time, TimeSpan.FromSeconds(2), gate, () => stopped++);
        gate.SafetyWindow = window;
        window.Start();
        time.Advance(TimeSpan.FromSeconds(3), fireTimers: false);
        if (renewFirst) Assert.False(window.Renew());
        Assert.Throws<InvalidOperationException>(gate.Open);
        Assert.False(gate.TryEnter(out _));
        Assert.False(window.Renew());
        time.Advance(TimeSpan.Zero);
        Assert.Equal(1, stopped);
    }

    [Fact]
    public void Successful_contact_renews_monotonic_deadline_despite_wall_clock_changes()
    {
        var time = new MembershipTestTimeProvider();
        var gate = new DistributedWorkAdmissionGate(time);
        var stopped = 0;
        using var window = new MembershipTableSafetyWindow(time, TimeSpan.FromSeconds(2), gate, () => stopped++);
        gate.SafetyWindow = window;
        window.Start();
        gate.Open();
        time.Advance(TimeSpan.FromSeconds(1));
        time.UtcOffset = TimeSpan.FromDays(-1);
        Assert.True(window.Renew());
        time.Advance(TimeSpan.FromSeconds(1));
        time.UtcOffset = TimeSpan.FromDays(1);
        Assert.True(gate.IsOpen);
        Assert.Equal(0, stopped);
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.False(gate.IsOpen);
        Assert.Equal(1, stopped);
    }

    [Fact]
    public void Disposal_releases_timer_and_expiry_race_notifies_once()
    {
        var time = new MembershipTestTimeProvider();
        var gate = new DistributedWorkAdmissionGate(time);
        var stopped = 0;
        var window = new MembershipTableSafetyWindow(time, TimeSpan.FromSeconds(2), gate,
            () => Interlocked.Increment(ref stopped));
        window.Start();
        time.Advance(TimeSpan.FromSeconds(2), fireTimers: false);
        Parallel.For(0, 50, i => { if (i % 2 == 0) window.Renew(); else window.Check(); });
        Assert.Equal(1, stopped);
        window.Dispose();
        Assert.Equal(0, time.ActiveTimers);
        time.Advance(TimeSpan.FromDays(1));
        Assert.Equal(1, stopped);
    }
}

internal sealed class MembershipTestTimeProvider : TimeProvider
{
    private readonly List<ManualTimer> timers = [];
    private long timestamp;
    public TimeSpan UtcOffset { get; set; }
    public TaskCompletionSource DelayScheduled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => Interlocked.Read(ref timestamp);
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch + TimeSpan.FromTicks(GetTimestamp()) + UtcOffset;
    public int ActiveTimers { get { lock (timers) return timers.Count(t => !t.Disposed); } }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        lock (timers) timers.Add(timer);
        if (dueTime != Timeout.InfiniteTimeSpan) DelayScheduled.TrySetResult();
        return timer;
    }

    public void Advance(TimeSpan duration, bool fireTimers = true)
    {
        Interlocked.Add(ref timestamp, duration.Ticks);
        if (!fireTimers) return;
        ManualTimer[] current;
        lock (timers) current = timers.ToArray();
        foreach (var timer in current) timer.FireIfDue();
    }

    private sealed class ManualTimer(MembershipTestTimeProvider time, TimerCallback callback, object? state) : ITimer
    {
        private long due = long.MaxValue;
        public bool Disposed { get; private set; }
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            Assert.Equal(Timeout.InfiniteTimeSpan, period);
            lock (this)
            {
                if (Disposed) return false;
                due = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : time.GetTimestamp() + dueTime.Ticks;
                return true;
            }
        }
        public void FireIfDue()
        {
            lock (this)
            {
                if (Disposed || due > time.GetTimestamp()) return;
                due = long.MaxValue;
            }
            callback(state);
        }
        public void Dispose() { lock (this) Disposed = true; }
        public ValueTask DisposeAsync() { Dispose(); return default; }
    }
}
