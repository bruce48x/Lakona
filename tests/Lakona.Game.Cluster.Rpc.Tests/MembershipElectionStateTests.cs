using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc.Membership;
using Xunit;

namespace Lakona.Game.Cluster.Rpc.Tests;

public sealed class MembershipElectionStateTests
{
    [Fact]
    public void OneCurrentVoterCanReceiveAtMostOneVotePerTerm()
    {
        var cluster = new ClusterIncarnationId(
            Guid.Parse("77777777-7777-7777-7777-777777777777"));
        var local = CreateReference(cluster, "data-1", "11111111-aaaa-bbbb-cccc-111111111111");
        var candidateA = CreateReference(cluster, "data-2", "22222222-aaaa-bbbb-cccc-222222222222");
        var candidateB = CreateReference(cluster, "data-3", "33333333-aaaa-bbbb-cccc-333333333333");
        var membership = new TestMembership(CreateSnapshot(local, candidateA, candidateB));
        var election = new MembershipElectionState(local, membership, new MembershipReplicatedLog());

        var first = election.RequestVote(new MembershipVoteRequest(
            candidateA,
            local,
            term: 4,
            membership.Current.View,
            lastLogIndex: 0,
            lastLogTerm: 0));
        var repeated = election.RequestVote(new MembershipVoteRequest(
            candidateA,
            local,
            term: 4,
            membership.Current.View,
            lastLogIndex: 0,
            lastLogTerm: 0));
        var competing = election.RequestVote(new MembershipVoteRequest(
            candidateB,
            local,
            term: 4,
            membership.Current.View,
            lastLogIndex: 0,
            lastLogTerm: 0));

        Assert.True(first.Granted);
        Assert.True(repeated.Granted);
        Assert.False(competing.Granted);
        Assert.Equal(MembershipVoteRejection.AlreadyVoted, competing.Rejection);
        Assert.Equal(4, election.CurrentTerm);
    }

    [Fact]
    public void CandidateRequiresAnExactMajorityAndStepsDownForAHigherTerm()
    {
        var cluster = new ClusterIncarnationId(
            Guid.Parse("88888888-8888-8888-8888-888888888888"));
        var local = CreateReference(cluster, "data-1", "11111111-bbbb-cccc-dddd-111111111111");
        var voterA = CreateReference(cluster, "data-2", "22222222-bbbb-cccc-dddd-222222222222");
        var voterB = CreateReference(cluster, "data-3", "33333333-bbbb-cccc-dddd-333333333333");
        var membership = new TestMembership(CreateSnapshot(local, voterA, voterB));
        var election = new MembershipElectionState(local, membership, new MembershipReplicatedLog());

        var campaign = election.StartElection();

        Assert.Equal(1, campaign.Term);
        Assert.Equal(2, campaign.Requests.Count);
        Assert.Equal(MembershipElectionRole.Candidate, election.Role);

        Assert.True(election.RecordVote(new MembershipVoteReply(
            voterA,
            local,
            campaign.Term,
            membership.Current.View,
            granted: true)));
        Assert.Equal(MembershipElectionRole.Leader, election.Role);

        Assert.False(election.RecordVote(new MembershipVoteReply(
            voterA,
            local,
            campaign.Term,
            membership.Current.View,
            granted: true)));

        Assert.False(election.RecordVote(new MembershipVoteReply(
            voterB,
            local,
            campaign.Term + 1,
            membership.Current.View,
            granted: false)));
        Assert.Equal(MembershipElectionRole.Follower, election.Role);
        Assert.Equal(2, election.CurrentTerm);
    }

    [Fact]
    public void RecoveringVoterCannotCampaignOrRaiseTheReadyLeaderTerm()
    {
        var cluster = new ClusterIncarnationId(
            Guid.Parse("99999999-8888-7777-6666-555555555555"));
        var local = CreateReference(cluster, "data-1", "11111111-cccc-dddd-eeee-111111111111");
        var recovering = CreateReference(cluster, "data-2", "22222222-cccc-dddd-eeee-222222222222");
        var membership = new TestMembership(new ClusterMembershipSnapshot(
            cluster,
            new MembershipViewId(9),
            [
                new ClusterMember(
                    local,
                    ClusterMemberState.Ready,
                    new NodeEndpoint("tcp://data-1:21001"),
                    isVoter: true),
                new ClusterMember(
                    recovering,
                    ClusterMemberState.Recovering,
                    new NodeEndpoint("tcp://data-2:21001"),
                    isVoter: true)
            ]));
        var leaderElection = new MembershipElectionState(
            local,
            membership,
            new MembershipReplicatedLog());
        var recoveringElection = new MembershipElectionState(
            recovering,
            membership,
            new MembershipReplicatedLog());

        var leaderCampaign = leaderElection.StartElection();
        Assert.True(leaderElection.RecordVote(new MembershipVoteReply(
            recovering,
            local,
            leaderCampaign.Term,
            membership.Current.View,
            granted: true)));
        var request = new MembershipVoteRequest(
            recovering,
            local,
            term: leaderCampaign.Term + 1,
            membership.Current.View,
            lastLogIndex: 0,
            lastLogTerm: 0);

        Assert.Throws<InvalidOperationException>(() => recoveringElection.StartElection());
        var response = leaderElection.RequestVote(request);
        Assert.False(response.Granted);
        Assert.Equal(MembershipVoteRejection.CandidateNotReady, response.Rejection);
        Assert.Equal(leaderCampaign.Term, leaderElection.CurrentTerm);
        Assert.Equal(MembershipElectionRole.Leader, leaderElection.Role);
    }

    private static ClusterMembershipSnapshot CreateSnapshot(params NodeReference[] references)
    {
        return new ClusterMembershipSnapshot(
            references[0].Cluster,
            new MembershipViewId(9),
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
