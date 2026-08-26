using Lakona.Game.Server.Actors;
using Xunit;

namespace Lakona.Game.Server.Tests.Actors;

public sealed class ClusterInvocationLifetimeTests
{
    [Fact]
    public void Remaining_time_uses_monotonic_time_after_deadline_conversion()
    {
        var time = new ManualMonotonicTimeProvider(
            new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero));
        using var lifetime = ClusterInvocationLifetime.FromDeadline(
            time.GetUtcNow().AddSeconds(10),
            time,
            TestContext.Current.CancellationToken);

        time.JumpUtc(TimeSpan.FromDays(7));
        time.AdvanceMonotonic(TimeSpan.FromSeconds(2));

        Assert.Equal(TimeSpan.FromSeconds(8), lifetime.Remaining);
        Assert.False(lifetime.Token.IsCancellationRequested);
    }

    private sealed class ManualMonotonicTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset currentUtcNow = utcNow;
        private long timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow() => currentUtcNow;

        public override long GetTimestamp() => timestamp;

        public void JumpUtc(TimeSpan amount) => currentUtcNow += amount;

        public void AdvanceMonotonic(TimeSpan amount) => timestamp += amount.Ticks;

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period) => new NoopTimer();

        private sealed class NoopTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => default;
        }
    }
}
