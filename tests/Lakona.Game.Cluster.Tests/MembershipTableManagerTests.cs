using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Membership;
using Xunit;

namespace Lakona.Game.Cluster.Tests;

public sealed class MembershipTableManagerTests
{
    [Fact]
    public async Task JoinAndActivatePublishOnlyRoutableActiveMembers()
    {
        var table = new InMemoryMembershipTable();
        var (manager, state) = CreateManager(table, "server-1", "11111111-1111-1111-1111-111111111111", 21001);
        var cancellationToken = TestContext.Current.CancellationToken;

        var local = await manager.JoinAsync(cancellationToken);
        Assert.Equal(ClusterMemberState.Joining, Assert.Single(state.Current.Members).State);

        await manager.ActivateAsync(
            new Dictionary<string, string> { ["role"] = "gateway" },
            [],
            [],
            cancellationToken);

        var active = Assert.Single(state.Current.Members);
        Assert.Equal(local, active.Reference);
        Assert.Equal(ClusterMemberState.Active, active.State);
        Assert.Equal("gateway", active.Labels["role"]);
    }

    [Fact]
    public async Task RefreshCanSkipIntermediateVersionsButNeverMoveBackward()
    {
        var table = new InMemoryMembershipTable();
        var (first, firstState) = CreateManager(table, "server-1", "11111111-1111-1111-1111-111111111111", 21001);
        var (second, _) = CreateManager(table, "server-2", "22222222-2222-2222-2222-222222222222", 21002);
        var cancellationToken = TestContext.Current.CancellationToken;
        await first.JoinAsync(cancellationToken);
        var firstView = firstState.Current.View;
        await second.JoinAsync(cancellationToken);
        await second.ActivateAsync(null, [], [], cancellationToken);

        await first.RefreshAsync(cancellationToken);

        Assert.True(firstState.Current.View.Value >= firstView.Value + 2);
        Assert.Equal(2, firstState.Current.Members.Count);
    }

    [Fact]
    public async Task TwoIndependentSuspicionsAreRequiredToDeclareAThreeNodeTargetDead()
    {
        var table = new InMemoryMembershipTable();
        var (first, _) = CreateManager(table, "server-1", "11111111-1111-1111-1111-111111111111", 21001);
        var (second, _) = CreateManager(table, "server-2", "22222222-2222-2222-2222-222222222222", 21002);
        var (targetManager, _) = CreateManager(table, "server-3", "33333333-3333-3333-3333-333333333333", 21003);
        var cancellationToken = TestContext.Current.CancellationToken;
        await JoinAndActivateAsync(first, cancellationToken);
        await JoinAndActivateAsync(second, cancellationToken);
        var target = await JoinAndActivateAsync(targetManager, cancellationToken);

        Assert.False(await first.TrySuspectAsync(target, 2, TimeSpan.FromMinutes(3), cancellationToken));
        Assert.True(await second.TrySuspectAsync(target, 2, TimeSpan.FromMinutes(3), cancellationToken));
        await Assert.ThrowsAsync<ClusterMembershipFencedException>(async () =>
            await targetManager.RefreshAsync(cancellationToken));

        var snapshot = await table.ReadOrCreateAsync("game", cancellationToken);
        Assert.Equal(MembershipTableStatus.Dead, snapshot.Entries.Single(entry => entry.Reference == target).Status);
    }

    [Fact]
    public async Task VotesFromDeadObserverIncarnationsDoNotContributeToDeathDecision()
    {
        var table = new InMemoryMembershipTable();
        var (first, _) = CreateManager(table, "server-1", "11111111-1111-1111-1111-111111111111", 21001);
        var (second, _) = CreateManager(table, "server-2", "22222222-2222-2222-2222-222222222222", 21002);
        var (third, _) = CreateManager(table, "server-3", "33333333-3333-3333-3333-333333333333", 21003);
        var (targetManager, _) = CreateManager(table, "server-4", "44444444-4444-4444-4444-444444444444", 21004);
        var cancellationToken = TestContext.Current.CancellationToken;
        await JoinAndActivateAsync(first, cancellationToken);
        await JoinAndActivateAsync(second, cancellationToken);
        await JoinAndActivateAsync(third, cancellationToken);
        var target = await JoinAndActivateAsync(targetManager, cancellationToken);

        Assert.False(await first.TrySuspectAsync(target, 2, TimeSpan.FromMinutes(3), cancellationToken));
        await first.MarkDeadAsync(cancellationToken);
        Assert.False(await second.TrySuspectAsync(target, 2, TimeSpan.FromMinutes(3), cancellationToken));
        Assert.True(await third.TrySuspectAsync(target, 2, TimeSpan.FromMinutes(3), cancellationToken));

        var snapshot = await table.ReadOrCreateAsync("game", cancellationToken);
        var deadTarget = snapshot.Entries.Single(entry => entry.Reference == target);
        Assert.Equal(MembershipTableStatus.Dead, deadTarget.Status);
        Assert.DoesNotContain(deadTarget.SuspectVotes, vote => vote.Observer.Node == new NodeId("server-1"));
    }

    [Fact]
    public async Task NewerIncarnationAtomicallyFencesThePreviousStableNode()
    {
        var table = new InMemoryMembershipTable();
        var time = new MutableTimeProvider();
        var (first, _) = CreateManager(table, "server-1", "11111111-1111-1111-1111-111111111111", 21001, time);
        var cancellationToken = TestContext.Current.CancellationToken;
        var previous = await JoinAndActivateAsync(first, cancellationToken);
        time.Advance(TimeSpan.FromSeconds(1));
        var (restarted, restartedState) = CreateManager(table, "server-1", "22222222-2222-2222-2222-222222222222", 21001, time);

        var current = await restarted.JoinAsync(cancellationToken);

        Assert.NotEqual(previous, current);
        Assert.Equal(current, Assert.Single(restartedState.Current.Members).Reference);
        var rows = await table.ReadOrCreateAsync("game", cancellationToken);
        Assert.Equal(MembershipTableStatus.Dead, rows.Entries.Single(entry => entry.Reference == previous).Status);
        Assert.Equal(MembershipTableStatus.Joining, rows.Entries.Single(entry => entry.Reference == current).Status);
        await Assert.ThrowsAsync<ClusterMembershipFencedException>(async () =>
            await first.RefreshAsync(cancellationToken));
    }

    [Fact]
    public async Task Local_node_stays_fenced_after_its_dead_row_is_cleaned_up()
    {
        var table = new InMemoryMembershipTable();
        var (manager, _) = CreateManager(
            table,
            "server-1",
            "11111111-1111-1111-1111-111111111111",
            21001);
        var cancellationToken = TestContext.Current.CancellationToken;
        var local = await JoinAndActivateAsync(manager, cancellationToken);
        var active = await table.ReadOrCreateAsync("game", cancellationToken);
        var entry = active.Entries.Single(candidate => candidate.Reference == local);
        Assert.True(await table.TryUpdateAsync(
            "game",
            entry.WithStatus(MembershipTableStatus.Dead),
            entry.Version,
            active.Version,
            cancellationToken));
        Assert.Equal(1, await table.CleanupDefunctAsync(
            "game",
            entry.IAmAliveTime.AddSeconds(1),
            maximumRows: 1,
            cancellationToken));

        await Assert.ThrowsAsync<ClusterMembershipFencedException>(() =>
            manager.RefreshAsync(cancellationToken).AsTask());
    }

    [Fact]
    public async Task StorageGenerationAllowsRestartAfterWallClockMovesBackward()
    {
        var table = new InMemoryMembershipTable();
        var time = new MutableTimeProvider();
        var (first, _) = CreateManager(table, "server-1", "11111111-1111-1111-1111-111111111111", 21001, time);
        var cancellationToken = TestContext.Current.CancellationToken;
        var previous = await JoinAndActivateAsync(first, cancellationToken);
        time.Advance(TimeSpan.FromHours(-1));
        var (restarted, _) = CreateManager(table, "server-1", "22222222-2222-2222-2222-222222222222", 21001, time);

        var current = await restarted.JoinAsync(cancellationToken);

        Assert.NotEqual(previous, current);
        var rows = await table.ReadOrCreateAsync("game", cancellationToken);
        Assert.True(rows.Entries.Single(entry => entry.Reference == current).Generation
            > rows.Entries.Single(entry => entry.Reference == previous).Generation);
    }

    [Fact]
    public async Task JoiningNodeCanFenceOnlyAnActiveMemberWithAStaleTableHeartbeat()
    {
        var table = new InMemoryMembershipTable();
        var time = new MutableTimeProvider();
        var (targetManager, _) = CreateManager(table, "server-1", "11111111-1111-1111-1111-111111111111", 21001, time);
        var target = await JoinAndActivateAsync(targetManager, TestContext.Current.CancellationToken);
        var (joiningManager, _) = CreateManager(table, "server-2", "22222222-2222-2222-2222-222222222222", 21002, time);
        await joiningManager.JoinAsync(TestContext.Current.CancellationToken);

        Assert.False(await joiningManager.TryMarkDefunctAsync(
            target,
            TimeSpan.FromMinutes(10),
            TestContext.Current.CancellationToken));

        time.Advance(TimeSpan.FromMinutes(10).Add(TimeSpan.FromSeconds(1)));

        Assert.True(await joiningManager.TryMarkDefunctAsync(
            target,
            TimeSpan.FromMinutes(10),
            TestContext.Current.CancellationToken));
        var snapshot = await table.ReadOrCreateAsync("game", TestContext.Current.CancellationToken);
        Assert.Equal(MembershipTableStatus.Dead, snapshot.Entries.Single(entry => entry.Reference == target).Status);
    }

    private static async ValueTask<NodeReference> JoinAndActivateAsync(
        MembershipTableManager manager,
        CancellationToken cancellationToken)
    {
        var reference = await manager.JoinAsync(cancellationToken);
        await manager.ActivateAsync(null, [], [], cancellationToken);
        return reference;
    }

    private static (MembershipTableManager Manager, ClusterMembershipState State) CreateManager(
        IMembershipTable table,
        string nodeId,
        string incarnation,
        int port,
        TimeProvider? timeProvider = null)
    {
        var state = new ClusterMembershipState();
        var manager = new MembershipTableManager(
            "game",
            new NodeId(nodeId),
            new NodeIncarnationId(Guid.Parse(incarnation)),
            new NodeEndpoint($"tcp://127.0.0.1:{port}"),
            table,
            state,
            timeProvider ?? new FixedTimeProvider());
        return (manager, state);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.Parse("2026-08-24T00:00:00Z");
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.Parse("2026-08-24T00:00:00Z");
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }
}
