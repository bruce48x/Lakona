using Lakona.Game.Cluster;
using Xunit;

namespace Lakona.Game.Cluster.Rpc.Tests;

internal sealed class TestMembership(ClusterMembershipSnapshot current) : IClusterMembership
{
    public ClusterMembershipSnapshot Current { get; set; } = current;

    public ValueTask<ClusterMembershipSnapshot> WaitForChangeAsync(
        MembershipViewId after,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }
}

internal sealed class ManualTimeProvider : TimeProvider
{
    private long timestamp;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => timestamp;

    public void Advance(TimeSpan duration)
    {
        timestamp += duration.Ticks;
    }
}

internal static class ClusterTestWait
{
    public static async Task UntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var clock = TimeProvider.System;
        var started = clock.GetTimestamp();
        while (!predicate())
        {
            if (clock.GetElapsedTime(started) >= timeout)
            {
                throw new TimeoutException("The membership condition was not reached in time.");
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
}
