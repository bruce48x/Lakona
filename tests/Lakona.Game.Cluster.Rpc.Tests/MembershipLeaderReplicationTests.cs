using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc.Membership;
using Xunit;

namespace Lakona.Game.Cluster.Rpc.Tests;

public sealed class MembershipLeaderReplicationTests
{
    [Fact]
    public void EmptyHeartbeatRoundRefreshesProofWithoutWritingANoopEntry()
    {
        var cluster = new ClusterIncarnationId(
            Guid.Parse("eeeeeeee-9999-8888-7777-eeeeeeeeeeee"));
        var local = CreateReference(cluster, "data-1", "11111111-eeee-ffff-aaaa-111111111111");
        var voterA = CreateReference(cluster, "data-2", "22222222-eeee-ffff-aaaa-222222222222");
        var voterB = CreateReference(cluster, "data-3", "33333333-eeee-ffff-aaaa-333333333333");
        var membership = new StubMembership(CreateSnapshot(local, voterA, voterB));
        var log = new MembershipReplicatedLog();
        var election = new MembershipElectionState(local, membership, log);
        var campaign = election.StartElection();
        election.RecordVote(new MembershipVoteReply(
            voterA,
            local,
            campaign.Term,
            membership.Current.View,
            granted: true));
        var replication = new MembershipLeaderReplication(local, membership, election, log);

        var heartbeat = replication.BeginHeartbeat();

        Assert.Equal(2, heartbeat.Requests.Count);
        Assert.All(heartbeat.Requests, request => Assert.Empty(request.Batch.Entries));
        Assert.Equal(0, log.LastIndex);
        Assert.False(replication.TryIssueQuorumProof(TimeSpan.FromSeconds(2), out _));

        Assert.False(replication.RecordReply(new MembershipAppendReply(
            voterA,
            local,
            campaign.Term,
            membership.Current.View,
            heartbeat.Sequence,
            accepted: true,
            matchIndex: 0)));
        Assert.True(replication.TryIssueQuorumProof(TimeSpan.FromSeconds(2), out _));
        Assert.Equal(0, log.LastIndex);
    }

    [Fact]
    public void ProposalCommitsAndProducesProofOnlyAfterSameRoundMajority()
    {
        var cluster = new ClusterIncarnationId(
            Guid.Parse("aaaaaaaa-9999-8888-7777-666666666666"));
        var local = CreateReference(cluster, "data-1", "11111111-dddd-eeee-ffff-111111111111");
        var voterA = CreateReference(cluster, "data-2", "22222222-dddd-eeee-ffff-222222222222");
        var voterB = CreateReference(cluster, "data-3", "33333333-dddd-eeee-ffff-333333333333");
        var membership = new StubMembership(CreateSnapshot(local, voterA, voterB));
        var log = new MembershipReplicatedLog();
        var election = new MembershipElectionState(local, membership, log);
        var campaign = election.StartElection();
        election.RecordVote(new MembershipVoteReply(
            voterA,
            local,
            campaign.Term,
            membership.Current.View,
            granted: true));
        var replication = new MembershipLeaderReplication(local, membership, election, log);

        var proposal = replication.Propose("member-ready", new byte[] { 1 });

        Assert.Equal(2, proposal.Requests.Count);
        Assert.Equal(0, log.CommitIndex);
        Assert.False(replication.TryIssueQuorumProof(TimeSpan.FromSeconds(2), out _));

        var advanced = replication.RecordReply(new MembershipAppendReply(
            voterA,
            local,
            campaign.Term,
            membership.Current.View,
            proposal.Sequence,
            accepted: true,
            matchIndex: 1));

        Assert.True(advanced);
        Assert.Equal(1, log.CommitIndex);
        var committedView = membership.Current;
        membership.Current = new ClusterMembershipSnapshot(
            cluster,
            new MembershipViewId(7),
            committedView.Members);
        Assert.False(replication.TryIssueQuorumProof(TimeSpan.FromSeconds(2), out _));
        membership.Current = committedView;
        Assert.True(replication.TryIssueQuorumProof(
            TimeSpan.FromSeconds(2),
            out var proof));
        Assert.Equal(campaign.Term, proof!.Term);
        Assert.Equal(membership.Current.View, proof.View);
        Assert.Equal(proposal.Sequence, proof.Sequence);
        Assert.False(replication.TryIssueQuorumProof(TimeSpan.FromSeconds(2), out _));

        Assert.False(replication.RecordReply(new MembershipAppendReply(
            voterA,
            local,
            campaign.Term,
            membership.Current.View,
            proposal.Sequence,
            accepted: true,
            matchIndex: 1)));
    }

    private static ClusterMembershipSnapshot CreateSnapshot(params NodeReference[] references)
    {
        return new ClusterMembershipSnapshot(
            references[0].Cluster,
            new MembershipViewId(6),
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

    private sealed class StubMembership : IClusterMembership
    {
        public StubMembership(ClusterMembershipSnapshot current)
        {
            Current = current;
        }

        public ClusterMembershipSnapshot Current { get; set; }

        public ValueTask<ClusterMembershipSnapshot> WaitForChangeAsync(
            MembershipViewId after,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
