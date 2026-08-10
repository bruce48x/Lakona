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

        var heartbeat = replication.BeginReplicationRound();

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

    [Fact]
    public void Replication_round_recovers_the_exact_same_term_joint_proposal()
    {
        var cluster = new ClusterIncarnationId(
            Guid.Parse("aaaaaaaa-aaaa-bbbb-cccc-aaaaaaaaaaaa"));
        var local = CreateReference(cluster, "data-1", "11111111-aaaa-bbbb-cccc-111111111111");
        var oldMajorityPeer = CreateReference(cluster, "data-2", "22222222-aaaa-bbbb-cccc-222222222222");
        var oldMinorityPeer = CreateReference(cluster, "data-3", "33333333-aaaa-bbbb-cccc-333333333333");
        var newPeerA = CreateReference(cluster, "data-4", "44444444-aaaa-bbbb-cccc-444444444444");
        var newPeerB = CreateReference(cluster, "data-5", "55555555-aaaa-bbbb-cccc-555555555555");
        var membership = new StubMembership(CreateSnapshot(local, oldMajorityPeer, oldMinorityPeer));
        var log = new MembershipReplicatedLog();
        var election = ElectLeader(local, oldMajorityPeer, membership, log);
        var replication = new MembershipLeaderReplication(local, membership, election, log);
        var next = CreateSnapshot(
            new[] { local, newPeerA, newPeerB },
            new MembershipViewId(7));

        var proposal = replication.ProposeJointConfiguration("member-replace", new byte[] { 1 }, next);

        Assert.False(replication.RecordReply(new MembershipAppendReply(
            oldMajorityPeer, local, election.CurrentTerm, membership.Current.View,
            proposal.Sequence, accepted: true, matchIndex: 1)));
        Assert.Equal(0, log.CommitIndex);

        var recovery = replication.BeginReplicationRound();

        Assert.Equal(4, recovery.Requests.Count);
        Assert.Empty(Assert.Single(
            recovery.Requests,
            request => request.Target == oldMajorityPeer).Batch.Entries);
        Assert.All(recovery.Requests.Where(request => request.Target != oldMajorityPeer), request =>
        {
            var entry = Assert.Single(request.Batch.Entries);
            Assert.Equal(1, entry.Index);
            Assert.Equal(election.CurrentTerm, entry.Term);
            Assert.Equal("member-replace", entry.CommandKind);
            Assert.Equal(new byte[] { 1 }, entry.Payload.ToArray());
        });
        Assert.True(replication.RecordReply(new MembershipAppendReply(
            newPeerA, local, election.CurrentTerm, membership.Current.View,
            recovery.Sequence, accepted: true, matchIndex: 1)));
        Assert.Equal(1, log.CommitIndex);
    }

    [Fact]
    public void Learner_catch_up_cannot_replace_a_pending_joint_proposal()
    {
        var cluster = new ClusterIncarnationId(
            Guid.Parse("cccccccc-aaaa-bbbb-cccc-cccccccccccc"));
        var local = CreateReference(cluster, "data-1", "11111111-cccc-bbbb-cccc-111111111111");
        var voterA = CreateReference(cluster, "data-2", "22222222-cccc-bbbb-cccc-222222222222");
        var voterB = CreateReference(cluster, "data-3", "33333333-cccc-bbbb-cccc-333333333333");
        var learner = CreateReference(cluster, "gateway-1", "44444444-cccc-bbbb-cccc-444444444444");
        var newPeerA = CreateReference(cluster, "data-4", "55555555-cccc-bbbb-cccc-555555555555");
        var newPeerB = CreateReference(cluster, "data-5", "66666666-cccc-bbbb-cccc-666666666666");
        var membership = new StubMembership(new ClusterMembershipSnapshot(
            cluster,
            new MembershipViewId(6),
            new[]
            {
                CreateReadyMember(local), CreateReadyMember(voterA), CreateReadyMember(voterB),
                new ClusterMember(learner, ClusterMemberState.Joining,
                    new NodeEndpoint("tcp://gateway-1:21001"), isVoter: false)
            }));
        var log = new MembershipReplicatedLog();
        var election = ElectLeader(local, voterA, membership, log);
        var replication = new MembershipLeaderReplication(local, membership, election, log);
        replication.RecordLearnerTransfer(learner, new MembershipViewId(5));
        var next = new ClusterMembershipSnapshot(
            cluster,
            new MembershipViewId(7),
            new[] { CreateReadyMember(local), CreateReadyMember(newPeerA), CreateReadyMember(newPeerB) });
        replication.ProposeJointConfiguration("member-replace", new byte[] { 1 }, next);

        Assert.Throws<ClusterMembershipProposalUnavailableException>(
            () => replication.CreateLearnerCatchUpRequest(learner));
        var recovery = replication.BeginReplicationRound();
        Assert.Equal(4, recovery.Requests.Count);
        Assert.Equal(0, log.CommitIndex);
    }

    [Fact]
    public void Heartbeat_fails_closed_for_an_uncommitted_prior_term_proposal()
    {
        var cluster = new ClusterIncarnationId(
            Guid.Parse("bbbbbbbb-aaaa-bbbb-cccc-bbbbbbbbbbbb"));
        var local = CreateReference(cluster, "data-1", "11111111-bbbb-bbbb-cccc-111111111111");
        var voterA = CreateReference(cluster, "data-2", "22222222-bbbb-bbbb-cccc-222222222222");
        var voterB = CreateReference(cluster, "data-3", "33333333-bbbb-bbbb-cccc-333333333333");
        var membership = new StubMembership(CreateSnapshot(local, voterA, voterB));
        var log = new MembershipReplicatedLog();
        var election = ElectLeader(local, voterA, membership, log);
        var replication = new MembershipLeaderReplication(local, membership, election, log);

        replication.Propose("member-ready", new byte[] { 1 });
        election.ObserveLeader(election.CurrentTerm + 1);
        var recampaign = election.StartElection();
        election.RecordVote(new MembershipVoteReply(
            voterA, local, recampaign.Term, membership.Current.View, granted: true));

        Assert.Throws<InvalidOperationException>(() => replication.BeginReplicationRound());
        Assert.Equal(0, log.CommitIndex);
    }

    private static MembershipElectionState ElectLeader(
        NodeReference local,
        NodeReference voter,
        StubMembership membership,
        MembershipReplicatedLog log)
    {
        var election = new MembershipElectionState(local, membership, log);
        var campaign = election.StartElection();
        election.RecordVote(new MembershipVoteReply(
            voter, local, campaign.Term, membership.Current.View, granted: true));
        return election;
    }

    private static ClusterMembershipSnapshot CreateSnapshot(
        NodeReference[] references,
        MembershipViewId? view = null)
    {
        return new ClusterMembershipSnapshot(
            references[0].Cluster,
            view ?? new MembershipViewId(6),
            references.Select(reference => new ClusterMember(
                reference,
                ClusterMemberState.Ready,
                new NodeEndpoint($"tcp://{reference.Node.Value}:21001"),
                isVoter: true)).ToArray());
    }

    private static ClusterMembershipSnapshot CreateSnapshot(params NodeReference[] references) =>
        CreateSnapshot(references, null);

    private static ClusterMember CreateReadyMember(NodeReference reference) =>
        new ClusterMember(
            reference,
            ClusterMemberState.Ready,
            new NodeEndpoint($"tcp://{reference.Node.Value}:21001"),
            isVoter: true);

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
