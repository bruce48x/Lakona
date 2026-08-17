using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc.Membership;
using Xunit;

namespace Lakona.Game.Cluster.Rpc.Tests;

public sealed class MembershipAppendReceiverTests
{
    [Fact]
    public void ReceiverFencesStaleIncarnationBeforeTermOrLogMutation()
    {
        var cluster = new ClusterIncarnationId(
            Guid.Parse("99999999-9999-9999-9999-999999999999"));
        var local = CreateReference(cluster, "data-2", "22222222-cccc-dddd-eeee-222222222222");
        var leader = CreateReference(cluster, "data-1", "11111111-cccc-dddd-eeee-111111111111");
        var membership = new TestMembership(CreateSnapshot(local, leader));
        var log = new MembershipReplicatedLog();
        var election = new MembershipElectionState(local, membership, log);
        var receiver = new MembershipAppendReceiver(local, membership, election, log);
        var batch = new MembershipAppendBatch(
            previousIndex: 0,
            previousTerm: 0,
            leaderCommit: 1,
            new[]
            {
                new MembershipLogEntry(1, term: 2, "member-ready", new byte[] { 1 })
            });

        var accepted = receiver.Append(new MembershipAppendRequest(
            leader,
            local,
            term: 2,
            membership.Current.View,
            sequence: 1,
            batch));
        var staleLeader = CreateReference(
            cluster,
            "data-1",
            "aaaaaaaa-cccc-dddd-eeee-aaaaaaaaaaaa");
        var fenced = receiver.Append(new MembershipAppendRequest(
            staleLeader,
            local,
            term: 100,
            membership.Current.View,
            sequence: 2,
            new MembershipAppendBatch(1, 2, 1, Array.Empty<MembershipLogEntry>())));

        Assert.Equal(MembershipAppendReceiveStatus.Accepted, accepted.Status);
        Assert.Equal(MembershipAppendReceiveStatus.IdentityMismatch, fenced.Status);
        Assert.Equal(2, election.CurrentTerm);
        Assert.Equal(1, log.CommitIndex);
        Assert.Single(log.ReadCommittedAfter(0));
    }

    [Fact]
    public async Task ReceiverUsesTheReplicatedLogLifecycleBoundary()
    {
        var cluster = new ClusterIncarnationId(
            Guid.Parse("88888888-8888-8888-8888-888888888888"));
        var local = CreateReference(cluster, "data-2", "22222222-bbbb-cccc-dddd-222222222222");
        var leader = CreateReference(cluster, "data-1", "11111111-bbbb-cccc-dddd-111111111111");
        var membership = new TestMembership(CreateSnapshot(local, leader));
        var log = new MembershipReplicatedLog();
        var election = new MembershipElectionState(local, membership, log);
        var receiver = new MembershipAppendReceiver(local, membership, election, log);
        using var release = new ManualResetEventSlim();
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var holder = Task.Run(() =>
        {
            lock (log.SyncRoot)
            {
                held.TrySetResult();
                release.Wait(TestContext.Current.CancellationToken);
            }
        }, TestContext.Current.CancellationToken);
        await held.Task.WaitAsync(TestContext.Current.CancellationToken);

        var append = Task.Run(() => receiver.Append(new MembershipAppendRequest(
            leader,
            local,
            term: 1,
            membership.Current.View,
            sequence: 1,
            new MembershipAppendBatch(
                previousIndex: 0,
                previousTerm: 0,
                leaderCommit: 0,
                Array.Empty<MembershipLogEntry>()))),
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<TimeoutException>(() => append.WaitAsync(
            TimeSpan.FromMilliseconds(100),
            TestContext.Current.CancellationToken));
        release.Set();
        Assert.Equal(MembershipAppendReceiveStatus.Accepted, (await append).Status);
        await holder;
    }

    private static ClusterMembershipSnapshot CreateSnapshot(params NodeReference[] references)
    {
        return new ClusterMembershipSnapshot(
            references[0].Cluster,
            new MembershipViewId(5),
            references.Select(reference => new ClusterMember(
                reference,
                ClusterMemberState.Ready,
                new NodeEndpoint($"tcp://{reference.Node.Value}:21001"),
                isVoter: true)).ToArray());
    }

    private static NodeReference CreateReference(
        ClusterIncarnationId cluster,
        string node,
        string incarnation)
    {
        return new NodeReference(
            cluster,
            new NodeId(node),
            new NodeIncarnationId(Guid.Parse(incarnation)));
    }

}
