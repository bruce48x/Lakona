using Lakona.Game.Cluster;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Testing.Tests;

public sealed class MembershipViewControlTests
{
    [Fact]
    public async Task Arrival_threshold_includes_canceled_waits_and_resume_preserves_the_count()
    {
        await using var fixture = new MembershipFixture();
        var visible = fixture.Membership.Current.View;
        fixture.Control.Pause("node");
        fixture.Advance();
        var threshold = fixture.Control.WaitForBlockedWaiterCountAsync("node", 2, fixture.Token);
        using var canceled = new CancellationTokenSource();
        var first = fixture.WaitAsync(visible, canceled.Token);
        await fixture.Control.WaitForBlockedWaiterCountAsync("node", 1, fixture.Token);

        Assert.False(first.IsCompleted);
        Assert.False(threshold.IsCompleted);
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.Equal(1, fixture.Control.GetBlockedWaiterCount("node"));
        Assert.False(threshold.IsCompleted);

        var second = fixture.WaitAsync(visible, fixture.Token);
        await threshold;
        Assert.False(second.IsCompleted);
        Assert.Equal(2, fixture.Control.GetBlockedWaiterCount("node"));
        fixture.Control.Resume("node");
        Assert.Equal(new MembershipViewId(2), (await second).View);
        Assert.Equal(2, fixture.Control.GetBlockedWaiterCount("node"));
    }

    [Fact]
    public async Task Next_pause_starts_a_new_arrival_count_and_threshold()
    {
        await using var fixture = new MembershipFixture();
        for (var cycle = 0; cycle < 2; cycle++)
        {
            var visible = fixture.Membership.Current.View;
            fixture.Control.Pause("node");
            Assert.Equal(0, fixture.Control.GetBlockedWaiterCount("node"));
            var threshold = fixture.Control.WaitForBlockedWaiterCountAsync("node", 1, fixture.Token);
            Assert.False(threshold.IsCompleted);
            fixture.Advance();
            var waiter = fixture.WaitAsync(visible, fixture.Token);
            await threshold;
            Assert.False(waiter.IsCompleted);
            Assert.Equal(1, fixture.Control.GetBlockedWaiterCount("node"));
            fixture.Control.Resume("node");
            Assert.Equal(new MembershipViewId(cycle + 2), (await waiter).View);
            Assert.Equal(1, fixture.Control.GetBlockedWaiterCount("node"));
        }
    }

    private sealed class MembershipFixture : IAsyncDisposable
    {
        private readonly CancellationTokenSource lifetime =
            CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        private readonly List<Task> waits = [];
        private readonly MutableMembership inner = new();
        private readonly ServiceProvider services;

        public MembershipFixture()
        {
            var registrations = new ServiceCollection();
            registrations.AddSingleton<IClusterMembership>(inner);
            Control.ConfigureNode(registrations, "node");
            services = registrations.BuildServiceProvider();
            Membership = services.GetRequiredService<IClusterMembership>();
        }

        public LakonaTestMembershipViewControl Control { get; } = new();
        public IClusterMembership Membership { get; }
        public CancellationToken Token => lifetime.Token;

        public void Advance() => inner.Advance();

        public Task<ClusterMembershipSnapshot> WaitAsync(MembershipViewId after, CancellationToken cancellationToken)
        {
            var wait = WaitCoreAsync(after, cancellationToken);
            waits.Add(wait);
            return wait;
        }

        private async Task<ClusterMembershipSnapshot> WaitCoreAsync(MembershipViewId after, CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(Token, cancellationToken);
            return await Membership.WaitForChangeAsync(after, linked.Token);
        }

        public async ValueTask DisposeAsync()
        {
            lifetime.Cancel();
            try
            {
                await Task.WhenAll(waits);
            }
            catch (OperationCanceledException) { }
            finally
            {
                await services.DisposeAsync();
                lifetime.Dispose();
            }
        }
    }

    private sealed class MutableMembership : IClusterMembership
    {
        public ClusterMembershipSnapshot Current { get; private set; } =
            new(new ClusterIncarnationId(Guid.NewGuid()), new MembershipViewId(1), []);

        public void Advance() => Current = new(Current.Cluster, new MembershipViewId(Current.View.Value + 1), []);

        public ValueTask<ClusterMembershipSnapshot> WaitForChangeAsync(MembershipViewId after, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Tests publish the next view before entering the real pause wrapper.
            Assert.True(Current.View.CompareTo(after) > 0);
            return new ValueTask<ClusterMembershipSnapshot>(Current);
        }
    }
}
