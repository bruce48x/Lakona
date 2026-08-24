using Lakona.Game.Cluster;
using Lakona.Game.Server.Hosting;
using Xunit;

namespace Lakona.Game.Server.Tests.Hosting;

public sealed class ClusterRecoveryBarrierTests
{
    [Fact]
    public async Task ParticipantsRunInStableOrderBeforeRecoveryCompletes()
    {
        var calls = new List<string>();
        var barrier = new ClusterRecoveryBarrier(new IClusterRecoveryParticipant[]
        {
            new RecordingParticipant("sessions", order: 20, calls),
            new RecordingParticipant("actors", order: 10, calls),
            new RecordingParticipant("timers", order: 30, calls)
        });

        await barrier.RecoverAsync(
            CreateContext(),
            TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "actors", "sessions", "timers" }, calls);
    }

    [Fact]
    public async Task FailureStopsTheBarrierAndIdentifiesTheParticipant()
    {
        var calls = new List<string>();
        var barrier = new ClusterRecoveryBarrier(new IClusterRecoveryParticipant[]
        {
            new RecordingParticipant("actors", order: 10, calls),
            new RecordingParticipant("sessions", order: 20, calls, shouldFail: true),
            new RecordingParticipant("timers", order: 30, calls)
        });

        var exception = await Assert.ThrowsAsync<ClusterRecoveryException>(async () =>
            await barrier.RecoverAsync(
                CreateContext(),
                TestContext.Current.CancellationToken));

        Assert.Equal("sessions", exception.ParticipantName);
        Assert.Equal(new[] { "actors", "sessions" }, calls);
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    private static ClusterRecoveryContext CreateContext()
    {
        var cluster = new ClusterIncarnationId(
            Guid.Parse("66666666-6666-6666-6666-666666666666"));
        var local = new NodeReference(
            cluster,
            new NodeId("data-1"),
            new NodeIncarnationId(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")));
        var snapshot = new ClusterMembershipSnapshot(
            cluster,
            new MembershipViewId(3),
            new[]
            {
                new ClusterMember(
                    local,
                    ClusterMemberState.Joining,
                    new NodeEndpoint("tcp://127.0.0.1:21001"))
            });
        return new ClusterRecoveryContext(local, snapshot);
    }

    private sealed class RecordingParticipant : IClusterRecoveryParticipant
    {
        private readonly List<string> calls;
        private readonly bool shouldFail;

        public RecordingParticipant(
            string name,
            int order,
            List<string> calls,
            bool shouldFail = false)
        {
            Name = name;
            Order = order;
            this.calls = calls;
            this.shouldFail = shouldFail;
        }

        public string Name { get; }

        public int Order { get; }

        public ValueTask RecoverAsync(
            ClusterRecoveryContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            calls.Add(Name);
            if (shouldFail)
            {
                throw new InvalidOperationException("recovery failed");
            }

            return default;
        }
    }
}
