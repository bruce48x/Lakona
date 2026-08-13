using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Xunit;

namespace Lakona.Game.Server.Tests.Actors;

public sealed class ActorLocationCoordinatorTests
{
    [Fact]
    public async Task New_membership_view_cancels_hung_stabilization_and_starts_latest_view()
    {
        var first = Snapshot(1);
        var second = Snapshot(2);
        var membership = new ControlledMembership(first);
        var stabilizer = new BlockingStabilizer();
        var coordinator = new ActorLocationCoordinator(
            membership,
            stabilizer,
            TimeSpan.FromSeconds(10));

        await coordinator.StartAsync(TestContext.Current.CancellationToken);
        await stabilizer.WaitForCallAsync(1, TestContext.Current.CancellationToken);
        membership.Publish(second);
        await stabilizer.WaitForCallAsync(2, TestContext.Current.CancellationToken);
        await coordinator.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal([new MembershipViewId(1), new MembershipViewId(2)], stabilizer.Views.Take(2));
        Assert.Contains(new MembershipViewId(1), stabilizer.CanceledViews);
    }

    [Fact]
    public async Task Stabilization_deadline_retries_the_latest_view()
    {
        var membership = new ControlledMembership(Snapshot(3));
        var stabilizer = new BlockingStabilizer();
        var coordinator = new ActorLocationCoordinator(
            membership,
            stabilizer,
            TimeSpan.FromMilliseconds(20));

        await coordinator.StartAsync(TestContext.Current.CancellationToken);
        await stabilizer.WaitForCallAsync(2, TestContext.Current.CancellationToken);
        await coordinator.StopAsync(TestContext.Current.CancellationToken);

        Assert.All(stabilizer.Views.Take(2), view => Assert.Equal(new MembershipViewId(3), view));
        Assert.Contains(new MembershipViewId(3), stabilizer.CanceledViews);
    }

    private static ClusterMembershipSnapshot Snapshot(long view) => new(
        new ClusterIncarnationId(Guid.Parse("61000000-0000-0000-0000-000000000000")),
        new MembershipViewId(view),
        []);

    private sealed class ControlledMembership(ClusterMembershipSnapshot current) : IClusterMembership
    {
        private TaskCompletionSource<ClusterMembershipSnapshot> next = NewSource();

        public ClusterMembershipSnapshot Current { get; private set; } = current;

        public ValueTask<ClusterMembershipSnapshot> WaitForChangeAsync(
            MembershipViewId observedView,
            CancellationToken cancellationToken = default)
        {
            if (Current.View.Value > observedView.Value)
                return new ValueTask<ClusterMembershipSnapshot>(Current);
            return new ValueTask<ClusterMembershipSnapshot>(next.Task.WaitAsync(cancellationToken));
        }

        public void Publish(ClusterMembershipSnapshot snapshot)
        {
            Current = snapshot;
            var completed = next;
            next = NewSource();
            completed.TrySetResult(snapshot);
        }

        private static TaskCompletionSource<ClusterMembershipSnapshot> NewSource() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class BlockingStabilizer : IActorLocationStabilizer
    {
        private readonly object gate = new();
        private readonly SemaphoreSlim calls = new(0);

        public List<MembershipViewId> Views { get; } = [];
        public HashSet<MembershipViewId> CanceledViews { get; } = [];

        public void ObserveRecoveryView(ClusterMembershipSnapshot snapshot)
        {
        }

        public async ValueTask StabilizeAsync(
            ClusterMembershipSnapshot snapshot,
            int maximumConcurrency,
            CancellationToken cancellationToken)
        {
            lock (gate)
            {
                Views.Add(snapshot.View);
            }
            calls.Release();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                lock (gate)
                {
                    CanceledViews.Add(snapshot.View);
                }
                throw;
            }
        }

        public async Task WaitForCallAsync(int count, CancellationToken cancellationToken)
        {
            while (true)
            {
                lock (gate)
                {
                    if (Views.Count >= count) return;
                }
                await calls.WaitAsync(cancellationToken);
            }
        }
    }
}
