using Lakona.Game.Cluster;
using Lakona.Game.Server.Hosting;
using Xunit;

namespace Lakona.Game.Server.Tests.Hosting;

public sealed class ClusterAuthorityCoordinatorTests
{
    [Fact]
    public async Task RecoveringAuthorityRunsBarrierAndCommitsReadyWithoutOpeningTraffic()
    {
        var fixture = new Fixture();

        await fixture.Coordinator.OnAuthorityAvailableAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "actors", "sessions" }, fixture.RecoveryCalls);
        Assert.Equal(1, fixture.Completion.CommitCount);
        Assert.Equal(ClusterMemberState.Ready, Assert.Single(fixture.Membership.Current.Members).State);
        Assert.False(fixture.Gate.IsOpen);

        await fixture.Coordinator.OnAuthorityAvailableAsync(
            TestContext.Current.CancellationToken);

        Assert.True(fixture.Gate.IsOpen);
    }

    [Fact]
    public async Task AuthorityLossClosesTrafficBeforeWaitingForInflightWork()
    {
        var fixture = new Fixture();
        await fixture.Coordinator.OnAuthorityAvailableAsync(
            TestContext.Current.CancellationToken);
        await fixture.Coordinator.OnAuthorityAvailableAsync(
            TestContext.Current.CancellationToken);
        Assert.True(fixture.Gate.TryEnter(out var admission));

        var lost = fixture.Coordinator.OnAuthorityLostAsync(
            TestContext.Current.CancellationToken).AsTask();

        Assert.False(fixture.Gate.IsOpen);
        Assert.False(lost.IsCompleted);

        fixture.Gate.Exit(admission);
        await lost;
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            var cluster = new ClusterIncarnationId(
                Guid.Parse("cccccccc-1111-2222-3333-cccccccccccc"));
            Local = new NodeReference(
                cluster,
                new NodeId("data-1"),
                new NodeIncarnationId(Guid.Parse("dddddddd-1111-2222-3333-dddddddddddd")));
            Membership = new TestClusterMembership(CreateSnapshot(
                Local,
                new MembershipViewId(1),
                ClusterMemberState.Recovering));
            Gate = new DistributedWorkAdmissionGate();
            Completion = new RecordingCompletion(Membership, Local);
            var barrier = new ClusterRecoveryBarrier(new IClusterRecoveryParticipant[]
            {
                new RecordingParticipant("sessions", 20, RecoveryCalls),
                new RecordingParticipant("actors", 10, RecoveryCalls)
            });
            Coordinator = new ClusterAuthorityCoordinator(
                Local,
                Membership,
                Gate,
                barrier,
                Completion,
                TimeSpan.FromSeconds(30));
        }

        public NodeReference Local { get; }

        public TestClusterMembership Membership { get; }

        public DistributedWorkAdmissionGate Gate { get; }

        public RecordingCompletion Completion { get; }

        public List<string> RecoveryCalls { get; } = new();

        public ClusterAuthorityCoordinator Coordinator { get; }
    }

    private sealed class RecordingCompletion : IClusterRecoveryCompletion
    {
        private readonly TestClusterMembership membership;
        private readonly NodeReference local;

        public RecordingCompletion(TestClusterMembership membership, NodeReference local)
        {
            this.membership = membership;
            this.local = local;
        }

        public int CommitCount { get; private set; }

        public ValueTask CommitReadyAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CommitCount++;
            membership.Current = CreateSnapshot(
                local,
                new MembershipViewId(membership.Current.View.Value + 1),
                ClusterMemberState.Ready);
            return default;
        }
    }

    private sealed class RecordingParticipant : IClusterRecoveryParticipant
    {
        private readonly List<string> calls;

        public RecordingParticipant(string name, int order, List<string> calls)
        {
            Name = name;
            Order = order;
            this.calls = calls;
        }

        public string Name { get; }

        public int Order { get; }

        public ValueTask RecoverAsync(
            ClusterRecoveryContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            calls.Add(Name);
            return default;
        }
    }

    private static ClusterMembershipSnapshot CreateSnapshot(
        NodeReference local,
        MembershipViewId view,
        ClusterMemberState state)
    {
        return new ClusterMembershipSnapshot(
            local.Cluster,
            view,
            new[]
            {
                new ClusterMember(
                    local,
                    state,
                    new NodeEndpoint("tcp://127.0.0.1:21001"),
                    isVoter: true)
            });
    }
}
