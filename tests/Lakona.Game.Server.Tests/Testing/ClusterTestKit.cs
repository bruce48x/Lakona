using Lakona.Game.Cluster;
using Xunit;

namespace Lakona.Game.Server.Tests.Testing;

internal sealed class TestClusterMembership(ClusterMembershipSnapshot current) : IClusterMembership
{
    public ClusterMembershipSnapshot Current { get; set; } = current;

    public ValueTask<ClusterMembershipSnapshot> WaitForChangeAsync(
        MembershipViewId after,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }
}

internal sealed class ImmediateTestClusterMembership(ClusterMembershipSnapshot current)
    : IClusterMembership
{
    public ClusterMembershipSnapshot Current { get; set; } = current;

    public ValueTask<ClusterMembershipSnapshot> WaitForChangeAsync(
        MembershipViewId after,
        CancellationToken cancellationToken = default)
    {
        return new ValueTask<ClusterMembershipSnapshot>(Current);
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
                throw new TimeoutException("The cluster condition was not reached in time.");
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
}
