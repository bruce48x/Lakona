using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc.Membership;
using Xunit;

namespace Lakona.Game.Cluster.Rpc.Tests;

public sealed class ClusterMembershipNodeTests
{
    [Fact]
    public async Task SingleNodeBootstrapCommitsReadyAndReprovesTheNewViewBeforeActivation()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var node = ClusterMembershipNode.BootstrapNewCluster(
            new NodeId("data-1"),
            new NodeEndpoint("tcp://127.0.0.1:21001"),
            new ClusterMembershipNodeOptions
            {
                HeartbeatInterval = TimeSpan.FromMilliseconds(1),
                ProofValidity = TimeSpan.FromSeconds(1),
                MinimumRetryDelay = TimeSpan.FromMilliseconds(1),
                MaximumRetryDelay = TimeSpan.FromMilliseconds(10)
            });
        var listener = new BootstrapListener(node, cancellation);

        await node.RunAsync(listener, cancellation.Token).WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, listener.AvailableViews.Count);
        Assert.Equal(new MembershipViewId(1), listener.AvailableViews[0]);
        Assert.Equal(new MembershipViewId(2), listener.AvailableViews[1]);
        Assert.Equal(1, listener.LostCount);
        Assert.Equal(
            ClusterMemberState.Ready,
            Assert.Single(node.Membership.Current.Members).State);
    }

    [Fact]
    public void LeaderAdmitsANewIncarnationAsACommittedNonVotingLearner()
    {
        var leader = ClusterMembershipNode.BootstrapNewCluster(
            new NodeId("data-1"),
            new NodeEndpoint("tcp://data-1:21001"));
        var incarnation = new NodeIncarnationId(
            Guid.Parse("99999999-1111-2222-3333-999999999999"));

        var joined = leader.AdmitLearner(
            new NodeId("data-2"),
            incarnation,
            new NodeEndpoint("tcp://data-2:21001"));

        Assert.Equal(new MembershipViewId(2), joined.View);
        Assert.Equal(2, joined.Members.Count);
        var learner = Assert.Single(
            joined.Members,
            member => member.Reference.Node == new NodeId("data-2"));
        Assert.Equal(incarnation, learner.Reference.Incarnation);
        Assert.Equal(ClusterMemberState.Joining, learner.State);
        Assert.False(learner.IsVoter);
    }

    [Fact]
    public async Task Direct_admission_exposes_a_typed_transient_when_its_voter_quorum_is_unavailable()
    {
        var leaderEndpoint = new NodeEndpoint("tcp://data-1:21001");
        var followerEndpoint = new NodeEndpoint("tcp://data-2:21001");
        var leader = ClusterMembershipNode.BootstrapNewCluster(new NodeId("data-1"), leaderEndpoint);
        var transport = new InMemoryMembershipTransport();
        transport.Register(leaderEndpoint, leader);
        await ElectSingleNodeLeaderAsync(leader, transport);
        var follower = await ClusterMembershipNode.JoinExistingClusterAsync(
            new NodeId("data-2"), followerEndpoint, [leaderEndpoint], transport,
            cancellationToken: TestContext.Current.CancellationToken);
        transport.Register(followerEndpoint, follower);
        await leader.PromoteLearnerAsync(follower.Local, transport, TestContext.Current.CancellationToken);

        var exception = Assert.Throws<ClusterMembershipProposalUnavailableException>(() =>
            leader.AdmitLearner(
                new NodeId("gateway-1"),
                NodeIncarnationId.New(),
                new NodeEndpoint("tcp://gateway-1:21001")));

        Assert.Contains("committed state machine", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ready_member_descriptor_refresh_commits_a_new_membership_view()
    {
        var endpoint = new NodeEndpoint("tcp://data-1:21001");
        var node = ClusterMembershipNode.BootstrapNewCluster(new NodeId("data-1"), endpoint);
        node.CommitLocalReady();
        var before = node.Membership.Current;
        var current = Assert.Single(before.Members);
        var descriptor = new ClusterMember(
            current.Reference,
            ClusterMemberState.Ready,
            endpoint,
            isVoter: true,
            labels: new Dictionary<string, string> { ["region"] = "cn" });

        var updated = await node.CommitMemberReadyDescriptorAsync(
            descriptor,
            new InMemoryMembershipTransport(),
            TestContext.Current.CancellationToken);

        Assert.True(updated.View.CompareTo(before.View) > 0);
        var member = Assert.Single(updated.Members);
        Assert.Equal("cn", member.Labels["region"]);
    }

    [Fact]
    public void LearnerRestoresTheCommittedSnapshotUnderItsExactIncarnation()
    {
        var leader = ClusterMembershipNode.BootstrapNewCluster(
            new NodeId("data-1"),
            new NodeEndpoint("tcp://data-1:21001"));
        var incarnation = new NodeIncarnationId(
            Guid.Parse("aaaaaaaa-4444-5555-6666-aaaaaaaaaaaa"));
        var committed = leader.AdmitLearner(
            new NodeId("data-2"),
            incarnation,
            new NodeEndpoint("tcp://data-2:21001"));
        var local = Assert.Single(
            committed.Members,
            member => member.Reference.Node == new NodeId("data-2")).Reference;

        var learner = ClusterMembershipNode.RestoreLearner(
            local,
            leader.CreateCatchUpTransfer());

        Assert.Equal(local, learner.Local);
        Assert.Equal(committed.Cluster, learner.Membership.Current.Cluster);
        Assert.Equal(committed.View, learner.Membership.Current.View);
        Assert.Equal(2, learner.Membership.Current.Members.Count);
        Assert.Equal(
            ClusterMemberState.Joining,
            Assert.Single(
                learner.Membership.Current.Members,
                member => member.Reference == local).State);
    }

    [Fact]
    public async Task JoinUsesAnUnorderedContactAndRestoresTheAdmittedLearner()
    {
        var leaderEndpoint = new NodeEndpoint("tcp://data-1:21001");
        var leader = ClusterMembershipNode.BootstrapNewCluster(
            new NodeId("data-1"),
            leaderEndpoint);
        var transport = new InMemoryMembershipTransport();
        transport.Register(leaderEndpoint, leader);
        await ElectSingleNodeLeaderAsync(leader, transport);

        var learner = await ClusterMembershipNode.JoinExistingClusterAsync(
            new NodeId("data-2"),
            new NodeEndpoint("tcp://data-2:21001"),
            new[]
            {
                new NodeEndpoint("tcp://unreachable:21001"),
                leaderEndpoint
            },
            transport,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(leader.Membership.Current.Cluster, learner.Membership.Current.Cluster);
        Assert.Equal(leader.Membership.Current.View, learner.Membership.Current.View);
        Assert.Equal(new NodeId("data-2"), learner.Local.Node);
        Assert.Equal(
            ClusterMemberState.Joining,
            Assert.Single(
                learner.Membership.Current.Members,
                member => member.Reference == learner.Local).State);
    }

    [Fact]
    public async Task LearnerPromotionRequiresItsReplicationAcknowledgement()
    {
        var leaderEndpoint = new NodeEndpoint("tcp://data-1:21001");
        var learnerEndpoint = new NodeEndpoint("tcp://data-2:21001");
        var leader = ClusterMembershipNode.BootstrapNewCluster(
            new NodeId("data-1"),
            leaderEndpoint);
        var transport = new InMemoryMembershipTransport();
        transport.Register(leaderEndpoint, leader);
        await ElectSingleNodeLeaderAsync(leader, transport);
        var learner = await ClusterMembershipNode.JoinExistingClusterAsync(
            new NodeId("data-2"),
            learnerEndpoint,
            new[] { leaderEndpoint },
            transport,
            cancellationToken: TestContext.Current.CancellationToken);
        transport.Register(learnerEndpoint, learner);

        var promoted = await leader.PromoteLearnerAsync(
            learner.Local,
            transport,
            TestContext.Current.CancellationToken);

        var leaderMember = Assert.Single(
            promoted.Members,
            member => member.Reference == learner.Local);
        Assert.True(leaderMember.IsVoter);
        Assert.Equal(ClusterMemberState.Recovering, leaderMember.State);
        var learnerMember = Assert.Single(
            learner.Membership.Current.Members,
            member => member.Reference == learner.Local);
        Assert.True(learnerMember.IsVoter);
        Assert.Equal(promoted.View, learner.Membership.Current.View);
    }

    [Fact]
    public async Task Three_node_promotion_retry_reuses_the_same_joint_entry_and_reaches_ready()
    {
        var leaderEndpoint = new NodeEndpoint("tcp://data-1:21001");
        var followerEndpoint = new NodeEndpoint("tcp://data-2:21001");
        var learnerEndpoint = new NodeEndpoint("tcp://gateway-1:21001");
        var leader = ClusterMembershipNode.BootstrapNewCluster(
            new NodeId("data-1"),
            leaderEndpoint);
        var transport = new InMemoryMembershipTransport();
        transport.Register(leaderEndpoint, leader);
        await ElectSingleNodeLeaderAsync(leader, transport);
        var follower = await JoinAndPromoteAsync(
            new NodeId("data-2"),
            followerEndpoint,
            leaderEndpoint,
            transport);
        var learner = await ClusterMembershipNode.JoinExistingClusterAsync(
            new NodeId("gateway-1"),
            learnerEndpoint,
            [leaderEndpoint],
            transport,
            cancellationToken: TestContext.Current.CancellationToken);
        transport.Register(learnerEndpoint, learner);

        var blockOldMajority = true;
        var promotionRequests = new List<MembershipAppendRequest>();
        transport.Intercept = (endpoint, request) =>
        {
            if (MembershipWireCodec.IsAppendRequest(request)
                && MembershipWireCodec.DecodeAppendRequest(request).Batch.Entries.Count > 0)
            {
                promotionRequests.Add(MembershipWireCodec.DecodeAppendRequest(request));
                return blockOldMajority && endpoint.Address == followerEndpoint.Address
                    ? MembershipWireCodec.EncodeMembershipUnavailableResponse()
                    : null;
            }

            return null;
        };

        await Assert.ThrowsAsync<AggregateException>(() =>
            learner.RequestPromotionAsync(
                [leaderEndpoint],
                transport,
                TestContext.Current.CancellationToken).AsTask());
        Assert.False(Assert.Single(
            leader.Membership.Current.Members,
            member => member.Reference == learner.Local).IsVoter);

        blockOldMajority = false;
        var promoted = await learner.RequestPromotionAsync(
            [leaderEndpoint],
            transport,
            TestContext.Current.CancellationToken);
        Assert.True(Assert.Single(
            promoted.Members,
            member => member.Reference == learner.Local).IsVoter);
        var initial = Assert.Single(promotionRequests[0].Batch.Entries);
        Assert.True(promotionRequests.Count >= 4);
        Assert.All(promotionRequests.Skip(1), request =>
        {
            var retry = Assert.Single(request.Batch.Entries);
            Assert.Equal(initial.Index, retry.Index);
            Assert.Equal(initial.Term, retry.Term);
            Assert.True(initial.Payload.Span.SequenceEqual(retry.Payload.Span));
        });

        transport.Intercept = null;
        await learner.RequestReadyAsync(
            [leaderEndpoint],
            transport,
            TestContext.Current.CancellationToken);
        Assert.Equal(
            ClusterMemberState.Ready,
            Assert.Single(
                learner.Membership.Current.Members,
                member => member.Reference == learner.Local).State);
    }

    [Fact]
    public async Task Joint_promotion_does_not_commit_with_only_the_old_voter_majority()
    {
        var leaderEndpoint = new NodeEndpoint("tcp://data-1:21001");
        var followerAEndpoint = new NodeEndpoint("tcp://data-2:21001");
        var followerBEndpoint = new NodeEndpoint("tcp://data-3:21001");
        var learnerEndpoint = new NodeEndpoint("tcp://gateway-1:21001");
        var leader = ClusterMembershipNode.BootstrapNewCluster(
            new NodeId("data-1"),
            leaderEndpoint);
        var transport = new InMemoryMembershipTransport();
        transport.Register(leaderEndpoint, leader);
        await ElectSingleNodeLeaderAsync(leader, transport);
        await JoinAndPromoteAsync(
            new NodeId("data-2"),
            followerAEndpoint,
            leaderEndpoint,
            transport);
        await JoinAndPromoteAsync(
            new NodeId("data-3"),
            followerBEndpoint,
            leaderEndpoint,
            transport);
        var learner = await ClusterMembershipNode.JoinExistingClusterAsync(
            new NodeId("gateway-1"),
            learnerEndpoint,
            [leaderEndpoint],
            transport,
            cancellationToken: TestContext.Current.CancellationToken);
        transport.Register(learnerEndpoint, learner);

        var blockNewMajority = true;
        transport.Intercept = (endpoint, request) =>
            blockNewMajority
            && MembershipWireCodec.IsAppendRequest(request)
            && MembershipWireCodec.DecodeAppendRequest(request).Batch.Entries.Count > 0
            && (endpoint.Address == followerBEndpoint.Address
                || endpoint.Address == learnerEndpoint.Address)
                ? MembershipWireCodec.EncodeMembershipUnavailableResponse()
                : null;

        await Assert.ThrowsAsync<AggregateException>(() =>
            learner.RequestPromotionAsync(
                [leaderEndpoint],
                transport,
                TestContext.Current.CancellationToken).AsTask());

        Assert.False(Assert.Single(
            leader.Membership.Current.Members,
            member => member.Reference == learner.Local).IsVoter);
        blockNewMajority = false;
        await learner.RequestPromotionAsync(
            [leaderEndpoint],
            transport,
            TestContext.Current.CancellationToken);
        Assert.True(Assert.Single(
            leader.Membership.Current.Members,
            member => member.Reference == learner.Local).IsVoter);
    }

    [Fact]
    public async Task Prior_term_joint_promotion_remains_fail_closed_after_leader_reelection()
    {
        var options = new ClusterMembershipNodeOptions
        {
            HeartbeatInterval = TimeSpan.FromMilliseconds(5),
            ProofValidity = TimeSpan.FromMilliseconds(20),
            MinimumRetryDelay = TimeSpan.FromMilliseconds(1),
            MaximumRetryDelay = TimeSpan.FromMilliseconds(5)
        };
        var leaderEndpoint = new NodeEndpoint("tcp://data-1:21001");
        var followerEndpoint = new NodeEndpoint("tcp://data-2:21001");
        var learnerEndpoint = new NodeEndpoint("tcp://gateway-1:21001");
        var leader = ClusterMembershipNode.BootstrapNewCluster(
            new NodeId("data-1"),
            leaderEndpoint,
            options);
        var transport = new InMemoryMembershipTransport();
        transport.Register(leaderEndpoint, leader);
        await ElectSingleNodeLeaderAsync(leader, transport);
        var follower = await JoinAndPromoteAsync(
            new NodeId("data-2"),
            followerEndpoint,
            leaderEndpoint,
            transport,
            options);
        var learner = await ClusterMembershipNode.JoinExistingClusterAsync(
            new NodeId("gateway-1"),
            learnerEndpoint,
            [leaderEndpoint],
            transport,
            options,
            cancellationToken: TestContext.Current.CancellationToken);
        transport.Register(learnerEndpoint, learner);

        MembershipAppendRequest? pendingRequest = null;
        var blockOldMajority = true;
        transport.Intercept = (endpoint, request) =>
        {
            if (!MembershipWireCodec.IsAppendRequest(request)
                || MembershipWireCodec.DecodeAppendRequest(request).Batch.Entries.Count == 0)
            {
                return null;
            }

            pendingRequest ??= MembershipWireCodec.DecodeAppendRequest(request);
            return blockOldMajority && endpoint.Address == followerEndpoint.Address
                ? MembershipWireCodec.EncodeMembershipUnavailableResponse()
                : null;
        };
        await Assert.ThrowsAsync<AggregateException>(() =>
            learner.RequestPromotionAsync(
                [leaderEndpoint],
                transport,
                TestContext.Current.CancellationToken).AsTask());
        var pending = Assert.IsType<MembershipAppendRequest>(pendingRequest);

        var higherTermAppend = new MembershipAppendRequest(
            follower.Local,
            leader.Local,
            pending.Term + 1,
            leader.Membership.Current.View,
            pending.Sequence + 1,
            new MembershipAppendBatch(
                pending.Batch.PreviousIndex,
                pending.Batch.PreviousTerm,
                pending.Batch.LeaderCommit,
                Array.Empty<MembershipLogEntry>()));
        await leader.HandleTransportRequestAsync(
            MembershipWireCodec.EncodeAppendRequest(higherTermAppend),
            transport,
            TestContext.Current.CancellationToken);
        Assert.False(leader.IsLeader);

        blockOldMajority = false;
        await Task.Delay(TimeSpan.FromMilliseconds(30), TestContext.Current.CancellationToken);
        using (var cancellation = new CancellationTokenSource())
        {
            var loop = leader.RunAsync(
                new PassiveAuthorityListener(),
                transport,
                cancellation.Token);
            await WaitUntilAsync(() => leader.IsLeader, TimeSpan.FromSeconds(2));
            await cancellation.CancelAsync();
            await loop;
        }

        var appendCountBeforeRetry = transport.NonEmptyAppendRequestCount;
        await Assert.ThrowsAsync<AggregateException>(() =>
            learner.RequestPromotionAsync(
                [leaderEndpoint],
                transport,
                TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(appendCountBeforeRetry, transport.NonEmptyAppendRequestCount);
        Assert.False(Assert.Single(
            leader.Membership.Current.Members,
            member => member.Reference == learner.Local).IsVoter);
    }

    [Fact]
    public async Task ConcurrentLearnersCatchUpAndPromoteSerially()
    {
        var leaderEndpoint = new NodeEndpoint("tcp://data-1:21001");
        var gatewayEndpoint = new NodeEndpoint("tcp://gateway-1:21001");
        var battleEndpoint = new NodeEndpoint("tcp://battle-1:21001");
        var leader = ClusterMembershipNode.BootstrapNewCluster(
            new NodeId("data-1"),
            leaderEndpoint);
        var transport = new InMemoryMembershipTransport();
        transport.Register(leaderEndpoint, leader);
        await ElectSingleNodeLeaderAsync(leader, transport);
        var gateway = await ClusterMembershipNode.JoinExistingClusterAsync(
            new NodeId("gateway-1"),
            gatewayEndpoint,
            [leaderEndpoint],
            transport,
            cancellationToken: TestContext.Current.CancellationToken);
        transport.Register(gatewayEndpoint, gateway);
        var battle = await ClusterMembershipNode.JoinExistingClusterAsync(
            new NodeId("battle-1"),
            battleEndpoint,
            [leaderEndpoint],
            transport,
            cancellationToken: TestContext.Current.CancellationToken);
        transport.Register(battleEndpoint, battle);

        await Task.WhenAll(
            leader.PromoteLearnerAsync(
                gateway.Local,
                transport,
                TestContext.Current.CancellationToken).AsTask(),
            leader.PromoteLearnerAsync(
                battle.Local,
                transport,
                TestContext.Current.CancellationToken).AsTask());

        Assert.All(
            leader.Membership.Current.Members.Where(member => member.Reference != leader.Local),
            member =>
            {
                Assert.True(member.IsVoter);
                Assert.Equal(ClusterMemberState.Recovering, member.State);
            });
        Assert.Equal(leader.Membership.Current.View, battle.Membership.Current.View);
    }

    [Fact]
    public async Task Heartbeat_repairs_a_voter_that_missed_a_membership_commit()
    {
        var leaderEndpoint = new NodeEndpoint("tcp://data-1:21001");
        var followerEndpoint = new NodeEndpoint("tcp://data-2:21001");
        var options = new ClusterMembershipNodeOptions
        {
            HeartbeatInterval = TimeSpan.FromMilliseconds(5),
            ProofValidity = TimeSpan.FromMilliseconds(100),
            MinimumRetryDelay = TimeSpan.FromMilliseconds(1),
            MaximumRetryDelay = TimeSpan.FromMilliseconds(10)
        };
        var leader = ClusterMembershipNode.BootstrapNewCluster(
            new NodeId("data-1"), leaderEndpoint, options);
        var transport = new InMemoryMembershipTransport();
        transport.Register(leaderEndpoint, leader);
        await ElectSingleNodeLeaderAsync(leader, transport);
        var follower = await ClusterMembershipNode.JoinExistingClusterAsync(
            new NodeId("data-2"),
            followerEndpoint,
            [leaderEndpoint],
            transport,
            options,
            cancellationToken: TestContext.Current.CancellationToken);
        transport.Register(followerEndpoint, follower);
        await leader.PromoteLearnerAsync(
            follower.Local,
            transport,
            TestContext.Current.CancellationToken);

        transport.DropNextEmptyAppendTo = followerEndpoint.Address;
        await leader.CommitMemberReadyAsync(
            follower.Local,
            transport,
            TestContext.Current.CancellationToken);

        Assert.NotEqual(leader.Membership.Current.View, follower.Membership.Current.View);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var authority = new SharedAuthorityListener(cancellation, expected: 1);
        await leader.RunAsync(authority, transport, cancellation.Token).WaitAsync(
            TimeSpan.FromSeconds(3),
            TestContext.Current.CancellationToken);

        Assert.Equal(leader.Membership.Current.View, follower.Membership.Current.View);
        Assert.Equal(
            ClusterMemberState.Ready,
            Assert.Single(
                follower.Membership.Current.Members,
                member => member.Reference == follower.Local).State);
    }

    [Fact]
    public async Task Admission_quorum_failure_returns_not_leader_and_heartbeat_recovers_the_same_join_once()
    {
        var leaderEndpoint = new NodeEndpoint("tcp://data-1:21001");
        var followerEndpoint = new NodeEndpoint("tcp://data-2:21001");
        var joiningEndpoint = new NodeEndpoint("tcp://gateway-1:21001");
        var options = new ClusterMembershipNodeOptions
        {
            HeartbeatInterval = TimeSpan.FromMilliseconds(5),
            ProofValidity = TimeSpan.FromMilliseconds(100),
            MinimumRetryDelay = TimeSpan.FromMilliseconds(1),
            MaximumRetryDelay = TimeSpan.FromMilliseconds(10)
        };
        var leader = ClusterMembershipNode.BootstrapNewCluster(
            new NodeId("data-1"), leaderEndpoint, options);
        var transport = new InMemoryMembershipTransport();
        transport.Register(leaderEndpoint, leader);
        await ElectSingleNodeLeaderAsync(leader, transport);
        var follower = await ClusterMembershipNode.JoinExistingClusterAsync(
            new NodeId("data-2"), followerEndpoint, [leaderEndpoint], transport, options,
            cancellationToken: TestContext.Current.CancellationToken);
        transport.Register(followerEndpoint, follower);
        await leader.PromoteLearnerAsync(
            follower.Local, transport, TestContext.Current.CancellationToken);
        await follower.RequestReadyAsync(
            [leaderEndpoint], transport, TestContext.Current.CancellationToken);

        var joiningNode = new NodeId("gateway-1");
        var joiningIncarnation = NodeIncarnationId.New();
        var joinRequest = MembershipWireCodec.EncodeJoinRequest(
            joiningNode, joiningIncarnation, joiningEndpoint);
        var proposalRequests = new List<MembershipAppendRequest>();
        transport.Intercept = (endpoint, request) =>
        {
            if (endpoint.Address == followerEndpoint.Address
                && MembershipWireCodec.IsAppendRequest(request)
                && MembershipWireCodec.DecodeAppendRequest(request).Batch.Entries.Count > 0)
            {
                proposalRequests.Add(MembershipWireCodec.DecodeAppendRequest(request));
                return proposalRequests.Count == 1
                    ? MembershipWireCodec.EncodeMembershipUnavailableResponse()
                    : null;
            }

            return null;
        };

        var before = leader.Membership.Current.View;
        var first = await leader.HandleTransportRequestAsync(
            joinRequest, transport, TestContext.Current.CancellationToken);
        var duplicate = await leader.HandleTransportRequestAsync(
            joinRequest, transport, TestContext.Current.CancellationToken);

        Assert.True(MembershipWireCodec.IsNotLeaderResponse(first));
        Assert.Null(MembershipWireCodec.DecodeNotLeaderResponse(first));
        Assert.True(MembershipWireCodec.IsNotLeaderResponse(duplicate));
        Assert.Null(MembershipWireCodec.DecodeNotLeaderResponse(duplicate));
        Assert.Single(proposalRequests);

        using var cancellation = new CancellationTokenSource();
        var loop = leader.RunAsync(new PassiveAuthorityListener(), transport, cancellation.Token);
        await WaitUntilAsync(
            () => leader.Membership.Current.Members.Any(member =>
                    member.Reference.Node == joiningNode
                    && member.Reference.Incarnation == joiningIncarnation)
                && follower.Membership.Current.Members.Any(member =>
                    member.Reference.Node == joiningNode
                    && member.Reference.Incarnation == joiningIncarnation),
            TimeSpan.FromSeconds(2));
        await cancellation.CancelAsync();
        await loop;

        Assert.Equal(before.Value + 1, leader.Membership.Current.View.Value);
        Assert.Equal(before.Value + 1, follower.Membership.Current.View.Value);
        Assert.Equal(
            ClusterMemberState.Joining,
            Assert.Single(
                leader.Membership.Current.Members,
                member => member.Reference.Node == joiningNode).State);
        Assert.Equal(
            ClusterMemberState.Joining,
            Assert.Single(
                follower.Membership.Current.Members,
                member => member.Reference.Node == joiningNode).State);
        var initial = proposalRequests[0];
        Assert.True(proposalRequests.Count >= 2);
        var initialEntry = Assert.Single(initial.Batch.Entries);
        Assert.All(proposalRequests.Skip(1), recovered =>
        {
            Assert.Equal(initial.Batch.PreviousIndex, recovered.Batch.PreviousIndex);
            Assert.Equal(initial.Batch.PreviousTerm, recovered.Batch.PreviousTerm);
            var recoveredEntry = Assert.Single(recovered.Batch.Entries);
            Assert.Equal(initialEntry.Index, recoveredEntry.Index);
            Assert.Equal(initialEntry.Term, recoveredEntry.Term);
            Assert.Equal(initialEntry.CommandKind, recoveredEntry.CommandKind);
            Assert.True(initialEntry.Payload.Span.SequenceEqual(recoveredEntry.Payload.Span));
        });
    }

    [Fact]
    public async Task Ready_quorum_failure_returns_not_leader_and_heartbeat_recovers_the_same_entry_once()
    {
        var leaderEndpoint = new NodeEndpoint("tcp://data-1:21001");
        var followerEndpoint = new NodeEndpoint("tcp://data-2:21001");
        var options = new ClusterMembershipNodeOptions
        {
            HeartbeatInterval = TimeSpan.FromMilliseconds(5),
            ProofValidity = TimeSpan.FromMilliseconds(100),
            MinimumRetryDelay = TimeSpan.FromMilliseconds(1),
            MaximumRetryDelay = TimeSpan.FromMilliseconds(10)
        };
        var leader = ClusterMembershipNode.BootstrapNewCluster(new NodeId("data-1"), leaderEndpoint, options);
        var transport = new InMemoryMembershipTransport();
        transport.Register(leaderEndpoint, leader);
        await ElectSingleNodeLeaderAsync(leader, transport);
        var follower = await ClusterMembershipNode.JoinExistingClusterAsync(
            new NodeId("data-2"), followerEndpoint, [leaderEndpoint], transport, options,
            cancellationToken: TestContext.Current.CancellationToken);
        transport.Register(followerEndpoint, follower);
        await leader.PromoteLearnerAsync(follower.Local, transport, TestContext.Current.CancellationToken);
        await follower.RequestReadyAsync([leaderEndpoint], transport, TestContext.Current.CancellationToken);

        var ready = new ClusterMember(
            follower.Local, ClusterMemberState.Ready, followerEndpoint, isVoter: true,
            labels: new Dictionary<string, string> { ["revision"] = "ready-recovery" });
        var proposalRequests = new List<MembershipAppendRequest>();
        transport.Intercept = (endpoint, request) =>
        {
            if (endpoint.Address == followerEndpoint.Address && MembershipWireCodec.IsAppendRequest(request)
                && MembershipWireCodec.DecodeAppendRequest(request).Batch.Entries.Count > 0)
            {
                proposalRequests.Add(MembershipWireCodec.DecodeAppendRequest(request));
                return proposalRequests.Count == 1 ? MembershipWireCodec.EncodeMembershipUnavailableResponse() : null;
            }
            return null;
        };
        var before = leader.Membership.Current.View;
        var first = await leader.HandleTransportRequestAsync(
            MembershipWireCodec.EncodeReadyRequest(ready), transport, TestContext.Current.CancellationToken);
        Assert.True(MembershipWireCodec.IsNotLeaderResponse(first));
        Assert.Null(MembershipWireCodec.DecodeNotLeaderResponse(first));
        Assert.Single(proposalRequests);

        using var cancellation = new CancellationTokenSource();
        var loop = leader.RunAsync(new PassiveAuthorityListener(), transport, cancellation.Token);
        await WaitUntilAsync(
            () => leader.Membership.Current.View.Value == before.Value + 1
                && follower.Membership.Current.View.Value == before.Value + 1,
            TimeSpan.FromSeconds(2));
        await cancellation.CancelAsync();
        await loop;

        Assert.Equal("ready-recovery", Assert.Single(leader.Membership.Current.Members, member => member.Reference == follower.Local).Labels!["revision"]);
        Assert.Equal("ready-recovery", Assert.Single(follower.Membership.Current.Members, member => member.Reference == follower.Local).Labels!["revision"]);
        var initial = Assert.Single(proposalRequests[0].Batch.Entries);
        Assert.All(proposalRequests.Skip(1), request =>
            Assert.True(Assert.Single(request.Batch.Entries).Payload.Span.SequenceEqual(initial.Payload.Span)));
    }

    [Fact]
    public async Task Concurrent_distinct_joins_map_the_in_flight_proposal_to_not_leader_without_a_second_proposal()
    {
        var leaderEndpoint = new NodeEndpoint("tcp://data-1:21001");
        var followerEndpoint = new NodeEndpoint("tcp://data-2:21001");
        var leader = ClusterMembershipNode.BootstrapNewCluster(new NodeId("data-1"), leaderEndpoint);
        var transport = new InMemoryMembershipTransport();
        transport.Register(leaderEndpoint, leader);
        await ElectSingleNodeLeaderAsync(leader, transport);
        var follower = await ClusterMembershipNode.JoinExistingClusterAsync(
            new NodeId("data-2"), followerEndpoint, [leaderEndpoint], transport,
            cancellationToken: TestContext.Current.CancellationToken);
        transport.Register(followerEndpoint, follower);
        await leader.PromoteLearnerAsync(follower.Local, transport, TestContext.Current.CancellationToken);
        await follower.RequestReadyAsync([leaderEndpoint], transport, TestContext.Current.CancellationToken);
        var learnerEndpoint = new NodeEndpoint("tcp://gateway-1:21001");
        var learner = await ClusterMembershipNode.JoinExistingClusterAsync(
            new NodeId("gateway-1"), learnerEndpoint, [leaderEndpoint], transport,
            cancellationToken: TestContext.Current.CancellationToken);
        transport.Register(learnerEndpoint, learner);

        var appendCountBefore = transport.NonEmptyAppendRequestCount;
        transport.DeferNextNonEmptyAppendTo = followerEndpoint.Address;
        var first = leader.HandleTransportRequestAsync(
            MembershipWireCodec.EncodeJoinRequest(
                new NodeId("battle-1"), NodeIncarnationId.New(),
                new NodeEndpoint("tcp://battle-1:21001")),
            transport,
            TestContext.Current.CancellationToken).AsTask();
        await WaitUntilAsync(() => transport.DeferredAppendStarted, TimeSpan.FromSeconds(2));

        var second = await leader.HandleTransportRequestAsync(
            MembershipWireCodec.EncodeJoinRequest(
                new NodeId("match-1"), NodeIncarnationId.New(),
                new NodeEndpoint("tcp://match-1:21001")),
            transport,
            TestContext.Current.CancellationToken);
        var promotion = await leader.HandleTransportRequestAsync(
            MembershipWireCodec.EncodePromoteRequest(
                learner.Local,
                learner.Membership.Current.View,
                learnerMatchIndex: 1),
            transport,
            TestContext.Current.CancellationToken);
        var ready = await leader.HandleTransportRequestAsync(
            MembershipWireCodec.EncodeReadyRequest(new ClusterMember(
                follower.Local,
                ClusterMemberState.Ready,
                followerEndpoint,
                isVoter: true,
                labels: new Dictionary<string, string> { ["revision"] = "2" })),
            transport,
            TestContext.Current.CancellationToken);
        transport.ReleaseDeferredAppend(MembershipWireCodec.EncodeMembershipUnavailableResponse());
        var firstResponse = await first;

        Assert.True(MembershipWireCodec.IsNotLeaderResponse(firstResponse));
        Assert.True(MembershipWireCodec.IsNotLeaderResponse(second));
        Assert.True(MembershipWireCodec.IsNotLeaderResponse(promotion));
        Assert.True(MembershipWireCodec.IsNotLeaderResponse(ready));
        Assert.Equal(appendCountBefore + 1, transport.NonEmptyAppendRequestCount);
        Assert.DoesNotContain(
            leader.Membership.Current.Members,
            member => member.Reference.Node == new NodeId("match-1"));
    }

    [Fact]
    public async Task TwoVotersReceiveAuthorityOnlyFromNetworkQuorumProofs()
    {
        var leaderEndpoint = new NodeEndpoint("tcp://data-1:21001");
        var learnerEndpoint = new NodeEndpoint("tcp://data-2:21001");
        var options = new ClusterMembershipNodeOptions
        {
            HeartbeatInterval = TimeSpan.FromMilliseconds(5),
            ProofValidity = TimeSpan.FromMilliseconds(100),
            MinimumRetryDelay = TimeSpan.FromMilliseconds(1),
            MaximumRetryDelay = TimeSpan.FromMilliseconds(10)
        };
        var leader = ClusterMembershipNode.BootstrapNewCluster(
            new NodeId("data-1"), leaderEndpoint, options);
        var transport = new InMemoryMembershipTransport();
        transport.Register(leaderEndpoint, leader);
        await ElectSingleNodeLeaderAsync(leader, transport);
        var follower = await ClusterMembershipNode.JoinExistingClusterAsync(
            new NodeId("data-2"),
            learnerEndpoint,
            new[] { leaderEndpoint },
            transport,
            options,
            cancellationToken: TestContext.Current.CancellationToken);
        transport.Register(learnerEndpoint, follower);
        await follower.RequestPromotionAsync(
            new[] { leaderEndpoint },
            transport,
            TestContext.Current.CancellationToken);
        await follower.RequestReadyAsync(
            new[] { leaderEndpoint },
            transport,
            TestContext.Current.CancellationToken);
        Assert.Equal(
            ClusterMemberState.Ready,
            Assert.Single(
                follower.Membership.Current.Members,
                member => member.Reference == follower.Local).State);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var authority = new SharedAuthorityListener(cancellation, expected: 2);

        var leaderLoop = leader.RunAsync(authority, transport, cancellation.Token);
        var followerLoop = follower.RunAsync(authority, transport, cancellation.Token);
        await Task.WhenAll(leaderLoop, followerLoop).WaitAsync(
            TimeSpan.FromSeconds(3),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, authority.Available);
    }

    [Fact]
    public async Task ThreeVotersElectAReplacementAfterTheLeaderDisappears()
    {
        var endpoint1 = new NodeEndpoint("tcp://data-1:21001");
        var endpoint2 = new NodeEndpoint("tcp://data-2:21001");
        var endpoint3 = new NodeEndpoint("tcp://data-3:21001");
        var options = new ClusterMembershipNodeOptions
        {
            HeartbeatInterval = TimeSpan.FromMilliseconds(7),
            ProofValidity = TimeSpan.FromMilliseconds(60),
            MinimumRetryDelay = TimeSpan.FromMilliseconds(2),
            MaximumRetryDelay = TimeSpan.FromMilliseconds(15)
        };
        var transport = new InMemoryMembershipTransport();
        var node1 = ClusterMembershipNode.BootstrapNewCluster(
            new NodeId("data-1"), endpoint1, options);
        transport.Register(endpoint1, node1);
        await ElectSingleNodeLeaderAsync(node1, transport);
        var node2 = await ClusterMembershipNode.JoinExistingClusterAsync(
            new NodeId("data-2"), endpoint2, new[] { endpoint1 }, transport, options,
            cancellationToken: TestContext.Current.CancellationToken);
        transport.Register(endpoint2, node2);
        await node2.RequestPromotionAsync(
            new[] { endpoint1 }, transport, TestContext.Current.CancellationToken);
        await node2.RequestReadyAsync(
            new[] { endpoint1 }, transport, TestContext.Current.CancellationToken);
        var node3 = await ClusterMembershipNode.JoinExistingClusterAsync(
            new NodeId("data-3"), endpoint3, new[] { endpoint1 }, transport, options,
            cancellationToken: TestContext.Current.CancellationToken);
        transport.Register(endpoint3, node3);
        await node3.RequestPromotionAsync(
            new[] { endpoint1 }, transport, TestContext.Current.CancellationToken);
        await node3.RequestReadyAsync(
            new[] { endpoint1 }, transport, TestContext.Current.CancellationToken);
        using var leaderCancellation = new CancellationTokenSource();
        using var followerCancellation = new CancellationTokenSource();
        var listener = new PassiveAuthorityListener();
        var leaderLoop = node1.RunAsync(listener, transport, leaderCancellation.Token);
        var node2Loop = node2.RunAsync(listener, transport, followerCancellation.Token);
        var node3Loop = node3.RunAsync(listener, transport, followerCancellation.Token);
        await WaitUntilAsync(
            () => listener.Available >= 3,
            TimeSpan.FromSeconds(2));

        await leaderCancellation.CancelAsync();
        await leaderLoop;
        transport.Unregister(endpoint1);
        await WaitUntilAsync(
            () => node2.IsLeader || node3.IsLeader,
            TimeSpan.FromSeconds(2));

        Assert.True(node2.IsLeader || node3.IsLeader);
        await followerCancellation.CancelAsync();
        await Task.WhenAll(node2Loop, node3Loop);
    }

    [Fact]
    public async Task MajorityEvictsAnUnreachableVoterAfterTheInternalGracePeriod()
    {
        var endpoint1 = new NodeEndpoint("tcp://data-1:21001");
        var endpoint2 = new NodeEndpoint("tcp://data-2:21001");
        var endpoint3 = new NodeEndpoint("tcp://data-3:21001");
        var options = new ClusterMembershipNodeOptions
        {
            HeartbeatInterval = TimeSpan.FromMilliseconds(5),
            ProofValidity = TimeSpan.FromMilliseconds(30),
            MemberEvictionGrace = TimeSpan.FromMilliseconds(90),
            MinimumRetryDelay = TimeSpan.FromMilliseconds(1),
            MaximumRetryDelay = TimeSpan.FromMilliseconds(10)
        };
        var transport = new InMemoryMembershipTransport();
        var node1 = ClusterMembershipNode.BootstrapNewCluster(
            new NodeId("data-1"), endpoint1, options);
        transport.Register(endpoint1, node1);
        await ElectSingleNodeLeaderAsync(node1, transport);
        var node2 = await ClusterMembershipNode.JoinExistingClusterAsync(
            new NodeId("data-2"), endpoint2, [endpoint1], transport, options,
            cancellationToken: TestContext.Current.CancellationToken);
        transport.Register(endpoint2, node2);
        await node2.RequestPromotionAsync(
            [endpoint1], transport, TestContext.Current.CancellationToken);
        await node2.RequestReadyAsync(
            [endpoint1], transport, TestContext.Current.CancellationToken);
        var node3 = await ClusterMembershipNode.JoinExistingClusterAsync(
            new NodeId("data-3"), endpoint3, [endpoint1], transport, options,
            cancellationToken: TestContext.Current.CancellationToken);
        transport.Register(endpoint3, node3);
        await node3.RequestPromotionAsync(
            [endpoint1], transport, TestContext.Current.CancellationToken);
        await node3.RequestReadyAsync(
            [endpoint1], transport, TestContext.Current.CancellationToken);

        using var majorityCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        using var failedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var listener = new PassiveAuthorityListener();
        var node1Loop = node1.RunAsync(listener, transport, majorityCancellation.Token);
        var node2Loop = node2.RunAsync(listener, transport, majorityCancellation.Token);
        var node3Loop = node3.RunAsync(listener, transport, failedCancellation.Token);
        await WaitUntilAsync(() => listener.Available >= 3, TimeSpan.FromSeconds(2));

        await failedCancellation.CancelAsync();
        await node3Loop;
        transport.Unregister(endpoint3);

        await WaitUntilAsync(
            () => node1.Membership.Current.Members.Count == 2
                && node2.Membership.Current.Members.Count == 2,
            TimeSpan.FromSeconds(2));

        Assert.DoesNotContain(
            node1.Membership.Current.Members,
            member => member.Reference == node3.Local);
        Assert.DoesNotContain(
            node2.Membership.Current.Members,
            member => member.Reference == node3.Local);

        await majorityCancellation.CancelAsync();
        await Task.WhenAll(node1Loop, node2Loop);
    }

    [Fact]
    public async Task RemovedIncarnationStopsWhenTheCurrentMajorityRejectsItsReturn()
    {
        var endpoint1 = new NodeEndpoint("tcp://data-1:21001");
        var endpoint2 = new NodeEndpoint("tcp://data-2:21001");
        var endpoint3 = new NodeEndpoint("tcp://data-3:21001");
        var options = new ClusterMembershipNodeOptions
        {
            HeartbeatInterval = TimeSpan.FromMilliseconds(5),
            ProofValidity = TimeSpan.FromMilliseconds(30),
            MemberEvictionGrace = TimeSpan.FromMilliseconds(90),
            MinimumRetryDelay = TimeSpan.FromMilliseconds(1),
            MaximumRetryDelay = TimeSpan.FromMilliseconds(10)
        };
        var transport = new InMemoryMembershipTransport();
        var node1 = ClusterMembershipNode.BootstrapNewCluster(
            new NodeId("data-1"), endpoint1, options);
        transport.Register(endpoint1, node1);
        await ElectSingleNodeLeaderAsync(node1, transport);
        var node2 = await ClusterMembershipNode.JoinExistingClusterAsync(
            new NodeId("data-2"), endpoint2, [endpoint1], transport, options,
            cancellationToken: TestContext.Current.CancellationToken);
        transport.Register(endpoint2, node2);
        await node2.RequestPromotionAsync(
            [endpoint1], transport, TestContext.Current.CancellationToken);
        await node2.RequestReadyAsync(
            [endpoint1], transport, TestContext.Current.CancellationToken);
        var removed = await ClusterMembershipNode.JoinExistingClusterAsync(
            new NodeId("data-3"), endpoint3, [endpoint1], transport, options,
            cancellationToken: TestContext.Current.CancellationToken);
        transport.Register(endpoint3, removed);
        await removed.RequestPromotionAsync(
            [endpoint1], transport, TestContext.Current.CancellationToken);
        await removed.RequestReadyAsync(
            [endpoint1], transport, TestContext.Current.CancellationToken);
        transport.Unregister(endpoint3);

        using var majorityCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var listener = new PassiveAuthorityListener();
        var node1Loop = node1.RunAsync(listener, transport, majorityCancellation.Token);
        var node2Loop = node2.RunAsync(listener, transport, majorityCancellation.Token);
        await WaitUntilAsync(
            () => node1.Membership.Current.Members.Count == 2
                && node2.Membership.Current.Members.Count == 2,
            TimeSpan.FromSeconds(2));

        transport.Register(endpoint3, removed);
        var exception = await Assert.ThrowsAsync<ClusterAuthorityFencingException>(
            () => removed.RunAsync(
                    new PassiveAuthorityListener(),
                    transport,
                    TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));

        Assert.Contains("removed", exception.Message, StringComparison.OrdinalIgnoreCase);

        await majorityCancellation.CancelAsync();
        await Task.WhenAll(node1Loop, node2Loop);
    }

    [Fact]
    public async Task VoterRecoveryStartsANewFullEvictionGracePeriod()
    {
        var endpoint1 = new NodeEndpoint("tcp://data-1:21001");
        var endpoint2 = new NodeEndpoint("tcp://data-2:21001");
        var endpoint3 = new NodeEndpoint("tcp://data-3:21001");
        var options = new ClusterMembershipNodeOptions
        {
            HeartbeatInterval = TimeSpan.FromMilliseconds(5),
            ProofValidity = TimeSpan.FromMilliseconds(30),
            MemberEvictionGrace = TimeSpan.FromMilliseconds(300),
            MinimumRetryDelay = TimeSpan.FromMilliseconds(1),
            MaximumRetryDelay = TimeSpan.FromMilliseconds(10)
        };
        var transport = new InMemoryMembershipTransport();
        var node1 = ClusterMembershipNode.BootstrapNewCluster(
            new NodeId("data-1"), endpoint1, options);
        transport.Register(endpoint1, node1);
        await ElectSingleNodeLeaderAsync(node1, transport);
        var node2 = await ClusterMembershipNode.JoinExistingClusterAsync(
            new NodeId("data-2"), endpoint2, [endpoint1], transport, options,
            cancellationToken: TestContext.Current.CancellationToken);
        transport.Register(endpoint2, node2);
        await node2.RequestPromotionAsync(
            [endpoint1], transport, TestContext.Current.CancellationToken);
        await node2.RequestReadyAsync(
            [endpoint1], transport, TestContext.Current.CancellationToken);
        var node3 = await ClusterMembershipNode.JoinExistingClusterAsync(
            new NodeId("data-3"), endpoint3, [endpoint1], transport, options,
            cancellationToken: TestContext.Current.CancellationToken);
        transport.Register(endpoint3, node3);
        await node3.RequestPromotionAsync(
            [endpoint1], transport, TestContext.Current.CancellationToken);
        await node3.RequestReadyAsync(
            [endpoint1], transport, TestContext.Current.CancellationToken);

        using var majorityCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        using var firstNode3Cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var listener = new PassiveAuthorityListener();
        var node1Loop = node1.RunAsync(listener, transport, majorityCancellation.Token);
        var node2Loop = node2.RunAsync(listener, transport, majorityCancellation.Token);
        var firstNode3Loop = node3.RunAsync(listener, transport, firstNode3Cancellation.Token);
        await WaitUntilAsync(() => listener.Available >= 3, TimeSpan.FromSeconds(2));

        await firstNode3Cancellation.CancelAsync();
        await firstNode3Loop;
        transport.Unregister(endpoint3);
        await Task.Delay(
            TimeSpan.FromMilliseconds(160),
            TestContext.Current.CancellationToken);
        Assert.Equal(3, node1.Membership.Current.Members.Count);

        transport.Register(endpoint3, node3);
        using var secondNode3Cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var secondNode3Loop = node3.RunAsync(
            new PassiveAuthorityListener(),
            transport,
            secondNode3Cancellation.Token);
        await Task.Delay(
            TimeSpan.FromMilliseconds(200),
            TestContext.Current.CancellationToken);
        Assert.Equal(3, node1.Membership.Current.Members.Count);

        await secondNode3Cancellation.CancelAsync();
        await secondNode3Loop;
        transport.Unregister(endpoint3);
        await Task.Delay(
            TimeSpan.FromMilliseconds(160),
            TestContext.Current.CancellationToken);
        Assert.Equal(3, node1.Membership.Current.Members.Count);
        await WaitUntilAsync(
            () => node1.Membership.Current.Members.Count == 2
                && node2.Membership.Current.Members.Count == 2,
            TimeSpan.FromSeconds(2));

        await majorityCancellation.CancelAsync();
        await Task.WhenAll(node1Loop, node2Loop);
    }

    [Fact]
    public async Task Restarted_stable_node_replaces_its_fenced_incarnation_through_joint_consensus()
    {
        var endpoint1 = new NodeEndpoint("tcp://data-1:21001");
        var endpoint2 = new NodeEndpoint("tcp://data-2:21001");
        var oldEndpoint3 = new NodeEndpoint("tcp://data-3:21001");
        var newEndpoint3 = new NodeEndpoint("tcp://data-3:22001");
        var options = new ClusterMembershipNodeOptions
        {
            HeartbeatInterval = TimeSpan.FromMilliseconds(5),
            ProofValidity = TimeSpan.FromMilliseconds(30),
            MinimumRetryDelay = TimeSpan.FromMilliseconds(1),
            MaximumRetryDelay = TimeSpan.FromMilliseconds(10)
        };
        var transport = new InMemoryMembershipTransport();
        var leader = ClusterMembershipNode.BootstrapNewCluster(
            new NodeId("data-1"), endpoint1, options);
        transport.Register(endpoint1, leader);
        await ElectSingleNodeLeaderAsync(leader, transport);
        var node2 = await ClusterMembershipNode.JoinExistingClusterAsync(
            new NodeId("data-2"), endpoint2, [endpoint1], transport, options,
            cancellationToken: TestContext.Current.CancellationToken);
        transport.Register(endpoint2, node2);
        await node2.RequestPromotionAsync([endpoint1], transport, TestContext.Current.CancellationToken);
        await node2.RequestReadyAsync([endpoint1], transport, TestContext.Current.CancellationToken);
        var oldNode3 = await ClusterMembershipNode.JoinExistingClusterAsync(
            new NodeId("data-3"), oldEndpoint3, [endpoint1], transport, options,
            cancellationToken: TestContext.Current.CancellationToken);
        transport.Register(oldEndpoint3, oldNode3);
        await oldNode3.RequestPromotionAsync([endpoint1], transport, TestContext.Current.CancellationToken);
        await oldNode3.RequestReadyAsync([endpoint1], transport, TestContext.Current.CancellationToken);
        var oldReference = oldNode3.Local;
        transport.Unregister(oldEndpoint3);

        var restarted = await ClusterMembershipNode.JoinExistingClusterAsync(
            new NodeId("data-3"), newEndpoint3, [endpoint1], transport, options,
            cancellationToken: TestContext.Current.CancellationToken);

        var current = leader.Membership.Current;
        Assert.False(current.TryGetMember(oldReference, out _));
        var replacement = Assert.Single(
            current.Members,
            member => member.Reference.Node == new NodeId("data-3"));
        Assert.Equal(restarted.Local, replacement.Reference);
        Assert.Equal(ClusterMemberState.Joining, replacement.State);
        Assert.False(replacement.IsVoter);
        Assert.Equal(current.View, node2.Membership.Current.View);
    }

    [Fact]
    public async Task Replacement_leader_installs_snapshot_when_a_learner_predates_its_retained_log()
    {
        var endpoint1 = new NodeEndpoint("tcp://data-1:21001");
        var staleEndpoint = new NodeEndpoint("tcp://stale:21001");
        var endpoint2 = new NodeEndpoint("tcp://data-2:21001");
        var endpoint3 = new NodeEndpoint("tcp://data-3:21001");
        var options = new ClusterMembershipNodeOptions
        {
            HeartbeatInterval = TimeSpan.FromMilliseconds(7),
            ProofValidity = TimeSpan.FromMilliseconds(60),
            MinimumRetryDelay = TimeSpan.FromMilliseconds(2),
            MaximumRetryDelay = TimeSpan.FromMilliseconds(15)
        };
        var transport = new InMemoryMembershipTransport();
        var node1 = ClusterMembershipNode.BootstrapNewCluster(
            new NodeId("data-1"), endpoint1, options);
        transport.Register(endpoint1, node1);
        await ElectSingleNodeLeaderAsync(node1, transport);

        var stale = await ClusterMembershipNode.JoinExistingClusterAsync(
            new NodeId("stale"), staleEndpoint, [endpoint1], transport, options,
            cancellationToken: TestContext.Current.CancellationToken);
        transport.Register(staleEndpoint, stale);

        var node2 = await ClusterMembershipNode.JoinExistingClusterAsync(
            new NodeId("data-2"), endpoint2, [endpoint1], transport, options,
            cancellationToken: TestContext.Current.CancellationToken);
        transport.Register(endpoint2, node2);
        await node2.RequestPromotionAsync(
            [endpoint1], transport, TestContext.Current.CancellationToken);
        await node2.RequestReadyAsync(
            [endpoint1], transport, TestContext.Current.CancellationToken);

        var node3 = await ClusterMembershipNode.JoinExistingClusterAsync(
            new NodeId("data-3"), endpoint3, [endpoint1], transport, options,
            cancellationToken: TestContext.Current.CancellationToken);
        transport.Register(endpoint3, node3);
        await node3.RequestPromotionAsync(
            [endpoint1], transport, TestContext.Current.CancellationToken);
        await node3.RequestReadyAsync(
            [endpoint1], transport, TestContext.Current.CancellationToken);

        using var oldLeaderCancellation = new CancellationTokenSource();
        using var replacementCancellation = new CancellationTokenSource();
        var listener = new PassiveAuthorityListener();
        var oldLeaderLoop = node1.RunAsync(listener, transport, oldLeaderCancellation.Token);
        var node2Loop = node2.RunAsync(listener, transport, replacementCancellation.Token);
        var node3Loop = node3.RunAsync(listener, transport, replacementCancellation.Token);
        await WaitUntilAsync(() => listener.Available >= 3, TimeSpan.FromSeconds(2));

        await oldLeaderCancellation.CancelAsync();
        await oldLeaderLoop;
        transport.Unregister(endpoint1);
        await WaitUntilAsync(() => node2.IsLeader || node3.IsLeader, TimeSpan.FromSeconds(2));
        var replacementEndpoint = node2.IsLeader ? endpoint2 : endpoint3;

        await stale.RequestPromotionAsync(
            [replacementEndpoint],
            transport,
            TestContext.Current.CancellationToken);

        Assert.True(Assert.Single(
            stale.Membership.Current.Members,
            member => member.Reference == stale.Local).IsVoter);

        await replacementCancellation.CancelAsync();
        await Task.WhenAll(node2Loop, node3Loop);
    }

    [Fact]
    public async Task NonLeaderIngressReturnsNotLeaderWithoutExecutingMutations()
    {
        var leaderEndpoint = new NodeEndpoint("tcp://data-1:21001");
        var battleEndpoint = new NodeEndpoint("tcp://battle-1:21001");
        var leader = ClusterMembershipNode.BootstrapNewCluster(
            new NodeId("data-1"), leaderEndpoint);
        var transport = new InMemoryMembershipTransport();
        transport.Register(leaderEndpoint, leader);
        await ElectSingleNodeLeaderAsync(leader, transport);
        var battle = await ClusterMembershipNode.JoinExistingClusterAsync(
            new NodeId("battle-1"),
            battleEndpoint,
            [leaderEndpoint],
            transport,
            cancellationToken: TestContext.Current.CancellationToken);
        transport.Register(battleEndpoint, battle);

        var join = await battle.HandleTransportRequestAsync(
            MembershipWireCodec.EncodeJoinRequest(
                new NodeId("gateway-1"),
                NodeIncarnationId.New(),
                new NodeEndpoint("tcp://gateway-1:21001")),
            transport,
            TestContext.Current.CancellationToken);
        Assert.True(MembershipWireCodec.IsNotLeaderResponse(join));
        Assert.Null(MembershipWireCodec.DecodeNotLeaderResponse(join));

        var promote = await battle.HandleTransportRequestAsync(
            MembershipWireCodec.EncodePromoteRequest(
                battle.Local,
                battle.Membership.Current.View,
                learnerMatchIndex: 1),
            transport,
            TestContext.Current.CancellationToken);
        Assert.True(MembershipWireCodec.IsNotLeaderResponse(promote));
        Assert.Null(MembershipWireCodec.DecodeNotLeaderResponse(promote));

        var ready = await battle.HandleTransportRequestAsync(
            MembershipWireCodec.EncodeReadyRequest(
                new ClusterMember(
                    battle.Local,
                    ClusterMemberState.Ready,
                    battleEndpoint,
                    isVoter: true)),
            transport,
            TestContext.Current.CancellationToken);
        Assert.True(MembershipWireCodec.IsNotLeaderResponse(ready));
        Assert.Null(MembershipWireCodec.DecodeNotLeaderResponse(ready));

        Assert.DoesNotContain(
            leader.Membership.Current.Members,
            member => member.Reference.Node == new NodeId("gateway-1"));
    }

    [Fact]
    public async Task JoinFollowsTheNotLeaderHintToTheLeaderOnce()
    {
        var leaderEndpoint = new NodeEndpoint("tcp://data-1:21001");
        var battleEndpoint = new NodeEndpoint("tcp://battle-1:21001");
        var gatewayEndpoint = new NodeEndpoint("tcp://gateway-1:21001");
        var leader = ClusterMembershipNode.BootstrapNewCluster(
            new NodeId("data-1"), leaderEndpoint);
        var transport = new InMemoryMembershipTransport();
        transport.Register(leaderEndpoint, leader);
        await ElectSingleNodeLeaderAsync(leader, transport);
        var battle = await ClusterMembershipNode.JoinExistingClusterAsync(
            new NodeId("battle-1"),
            battleEndpoint,
            [leaderEndpoint],
            transport,
            cancellationToken: TestContext.Current.CancellationToken);
        transport.Register(battleEndpoint, battle);
        await leader.PromoteLearnerAsync(
            battle.Local,
            transport,
            TestContext.Current.CancellationToken);

        // The only contact is a non-leader learner that cannot admit by itself, so
        // a successful join proves the client followed the NotLeader hint to the
        // leader exactly once.
        var gateway = await ClusterMembershipNode.JoinExistingClusterAsync(
            new NodeId("gateway-1"),
            gatewayEndpoint,
            [battleEndpoint],
            transport,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(leader.Membership.Current.Cluster, gateway.Membership.Current.Cluster);
        Assert.Equal(
            ClusterMemberState.Joining,
            Assert.Single(
                gateway.Membership.Current.Members,
                member => member.Reference == gateway.Local).State);
    }

    [Fact]
    public async Task JoinFailsWhenTheOnlyContactDoesNotKnowTheLeader()
    {
        var leaderEndpoint = new NodeEndpoint("tcp://data-1:21001");
        var battleEndpoint = new NodeEndpoint("tcp://battle-1:21001");
        var gatewayEndpoint = new NodeEndpoint("tcp://gateway-1:21001");
        var leader = ClusterMembershipNode.BootstrapNewCluster(
            new NodeId("data-1"), leaderEndpoint);
        var transport = new InMemoryMembershipTransport();
        transport.Register(leaderEndpoint, leader);
        await ElectSingleNodeLeaderAsync(leader, transport);
        var battle = await ClusterMembershipNode.JoinExistingClusterAsync(
            new NodeId("battle-1"),
            battleEndpoint,
            [leaderEndpoint],
            transport,
            cancellationToken: TestContext.Current.CancellationToken);
        transport.Register(battleEndpoint, battle);

        var before = transport.RequestCount;
        var exception = await Assert.ThrowsAsync<AggregateException>(() =>
            ClusterMembershipNode.JoinExistingClusterAsync(
                new NodeId("gateway-1"),
                gatewayEndpoint,
                [battleEndpoint],
                transport,
                cancellationToken: TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(1, transport.RequestCount - before);
        Assert.DoesNotContain(
            leader.Membership.Current.Members,
            member => member.Reference.Node == new NodeId("gateway-1"));
    }

    [Fact]
    public async Task JoinContinuesToTheNextContactWhenOneDoesNotKnowTheLeader()
    {
        // Mirrors the three-node startup topology: the first contact is a fresh
        // learner that does not know the leader, and the second contact is the
        // leader. A NotLeader result without an endpoint must not stop the round
        // or the cluster could never converge.
        var leaderEndpoint = new NodeEndpoint("tcp://data-1:21001");
        var battleEndpoint = new NodeEndpoint("tcp://battle-1:21001");
        var gatewayEndpoint = new NodeEndpoint("tcp://gateway-1:21001");
        var leader = ClusterMembershipNode.BootstrapNewCluster(
            new NodeId("data-1"), leaderEndpoint);
        var transport = new InMemoryMembershipTransport();
        transport.Register(leaderEndpoint, leader);
        await ElectSingleNodeLeaderAsync(leader, transport);
        var battle = await ClusterMembershipNode.JoinExistingClusterAsync(
            new NodeId("battle-1"),
            battleEndpoint,
            [leaderEndpoint],
            transport,
            cancellationToken: TestContext.Current.CancellationToken);
        transport.Register(battleEndpoint, battle);

        var gateway = await ClusterMembershipNode.JoinExistingClusterAsync(
            new NodeId("gateway-1"),
            gatewayEndpoint,
            [battleEndpoint, leaderEndpoint],
            transport,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(leader.Membership.Current.Cluster, gateway.Membership.Current.Cluster);
        Assert.Equal(
            ClusterMemberState.Joining,
            Assert.Single(
                gateway.Membership.Current.Members,
                member => member.Reference == gateway.Local).State);
    }

    [Fact]
    public async Task PromotionFollowsTheNotLeaderHintToTheLeaderOnce()
    {
        var leaderEndpoint = new NodeEndpoint("tcp://data-1:21001");
        var battleEndpoint = new NodeEndpoint("tcp://battle-1:21001");
        var gatewayEndpoint = new NodeEndpoint("tcp://gateway-1:21001");
        var leader = ClusterMembershipNode.BootstrapNewCluster(
            new NodeId("data-1"), leaderEndpoint);
        var transport = new InMemoryMembershipTransport();
        transport.Register(leaderEndpoint, leader);
        await ElectSingleNodeLeaderAsync(leader, transport);
        var battle = await ClusterMembershipNode.JoinExistingClusterAsync(
            new NodeId("battle-1"),
            battleEndpoint,
            [leaderEndpoint],
            transport,
            cancellationToken: TestContext.Current.CancellationToken);
        transport.Register(battleEndpoint, battle);
        var gateway = await ClusterMembershipNode.JoinExistingClusterAsync(
            new NodeId("gateway-1"),
            gatewayEndpoint,
            [leaderEndpoint],
            transport,
            cancellationToken: TestContext.Current.CancellationToken);
        transport.Register(gatewayEndpoint, gateway);
        await leader.PromoteLearnerAsync(
            battle.Local,
            transport,
            TestContext.Current.CancellationToken);

        // The only contact is a non-leader voter that cannot promote by itself, so
        // a successful promotion proves the client followed the NotLeader hint to
        // the leader exactly once.
        await gateway.RequestPromotionAsync(
            [battleEndpoint],
            transport,
            TestContext.Current.CancellationToken);

        Assert.True(Assert.Single(
            gateway.Membership.Current.Members,
            member => member.Reference == gateway.Local).IsVoter);
    }

    [Fact]
    public async Task PromotionStopsTheRoundWhenTheLeaderHintStillReturnsNotLeader()
    {
        var leaderEndpoint = new NodeEndpoint("tcp://data-1:21001");
        var battleEndpoint = new NodeEndpoint("tcp://battle-1:21001");
        var gatewayEndpoint = new NodeEndpoint("tcp://gateway-1:21001");
        var leader = ClusterMembershipNode.BootstrapNewCluster(
            new NodeId("data-1"), leaderEndpoint);
        var transport = new InMemoryMembershipTransport();
        transport.Register(leaderEndpoint, leader);
        await ElectSingleNodeLeaderAsync(leader, transport);
        var battle = await ClusterMembershipNode.JoinExistingClusterAsync(
            new NodeId("battle-1"),
            battleEndpoint,
            [leaderEndpoint],
            transport,
            cancellationToken: TestContext.Current.CancellationToken);
        transport.Register(battleEndpoint, battle);
        var gateway = await ClusterMembershipNode.JoinExistingClusterAsync(
            new NodeId("gateway-1"),
            gatewayEndpoint,
            [leaderEndpoint],
            transport,
            cancellationToken: TestContext.Current.CancellationToken);
        transport.Register(gatewayEndpoint, gateway);
        await leader.PromoteLearnerAsync(
            battle.Local,
            transport,
            TestContext.Current.CancellationToken);
        transport.Intercept = (endpoint, request) =>
            endpoint.Address == leaderEndpoint.Address
                && MembershipWireCodec.IsPromoteRequest(request)
                ? MembershipWireCodec.EncodeNotLeaderResponse(null)
                : null;

        var before = transport.RequestCount;
        var exception = await Assert.ThrowsAsync<AggregateException>(() =>
            gateway.RequestPromotionAsync(
                [battleEndpoint],
                transport,
                TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(2, transport.RequestCount - before);
    }

    [Fact]
    public async Task PromotionStopsTheRoundWhenTheFollowedHintResponseCannotBeDecoded()
    {
        var leaderEndpoint = new NodeEndpoint("tcp://data-1:21001");
        var battleEndpoint = new NodeEndpoint("tcp://battle-1:21001");
        var thirdEndpoint = new NodeEndpoint("tcp://data-2:21001");
        var gatewayEndpoint = new NodeEndpoint("tcp://gateway-1:21001");
        var leader = ClusterMembershipNode.BootstrapNewCluster(new NodeId("data-1"), leaderEndpoint);
        var transport = new InMemoryMembershipTransport();
        transport.Register(leaderEndpoint, leader);
        await ElectSingleNodeLeaderAsync(leader, transport);
        var battle = await ClusterMembershipNode.JoinExistingClusterAsync(new NodeId("battle-1"), battleEndpoint, [leaderEndpoint], transport, cancellationToken: TestContext.Current.CancellationToken);
        transport.Register(battleEndpoint, battle);
        var gateway = await ClusterMembershipNode.JoinExistingClusterAsync(new NodeId("gateway-1"), gatewayEndpoint, [leaderEndpoint], transport, cancellationToken: TestContext.Current.CancellationToken);
        transport.Register(gatewayEndpoint, gateway);
        await leader.PromoteLearnerAsync(battle.Local, transport, TestContext.Current.CancellationToken);
        transport.Intercept = (endpoint, request) => endpoint.Address == leaderEndpoint.Address && MembershipWireCodec.IsPromoteRequest(request)
            ? MembershipWireCodec.EncodeReadyResponse(leader.Membership.Current) : null;

        var before = transport.RequestCount;
        await Assert.ThrowsAsync<AggregateException>(() => gateway.RequestPromotionAsync(
            [battleEndpoint, thirdEndpoint], transport, TestContext.Current.CancellationToken).AsTask());
        Assert.Equal(2, transport.RequestCount - before);
    }

    [Fact]
    public async Task PromotionDoesNotFollowAStaleHintBackToAnAttemptedEndpoint()
    {
        var leaderEndpoint = new NodeEndpoint("tcp://data-1:21001");
        var battleEndpoint = new NodeEndpoint("tcp://battle-1:21001");
        var gatewayEndpoint = new NodeEndpoint("tcp://gateway-1:21001");
        var leader = ClusterMembershipNode.BootstrapNewCluster(
            new NodeId("data-1"), leaderEndpoint);
        var transport = new InMemoryMembershipTransport();
        transport.Register(leaderEndpoint, leader);
        await ElectSingleNodeLeaderAsync(leader, transport);
        var battle = await ClusterMembershipNode.JoinExistingClusterAsync(
            new NodeId("battle-1"),
            battleEndpoint,
            [leaderEndpoint],
            transport,
            cancellationToken: TestContext.Current.CancellationToken);
        transport.Register(battleEndpoint, battle);
        var gateway = await ClusterMembershipNode.JoinExistingClusterAsync(
            new NodeId("gateway-1"),
            gatewayEndpoint,
            [leaderEndpoint],
            transport,
            cancellationToken: TestContext.Current.CancellationToken);
        transport.Register(gatewayEndpoint, gateway);
        await leader.PromoteLearnerAsync(
            battle.Local,
            transport,
            TestContext.Current.CancellationToken);
        transport.Intercept = (endpoint, request) =>
            endpoint.Address == leaderEndpoint.Address
                && MembershipWireCodec.IsPromoteRequest(request)
                ? MembershipWireCodec.EncodeNotLeaderResponse(leaderEndpoint)
                : null;

        var before = transport.RequestCount;
        var exception = await Assert.ThrowsAsync<AggregateException>(() =>
            gateway.RequestPromotionAsync(
                [battleEndpoint],
                transport,
                TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(2, transport.RequestCount - before);
    }

    [Fact]
    public async Task PromotionFollowsAtMostOneLeaderHintPerRound()
    {
        var leaderEndpoint = new NodeEndpoint("tcp://data-1:21001");
        var battleEndpoint = new NodeEndpoint("tcp://battle-1:21001");
        var gatewayEndpoint = new NodeEndpoint("tcp://gateway-1:21001");
        var thirdEndpoint = new NodeEndpoint("tcp://data-2:21001");
        var leader = ClusterMembershipNode.BootstrapNewCluster(
            new NodeId("data-1"), leaderEndpoint);
        var transport = new InMemoryMembershipTransport();
        transport.Register(leaderEndpoint, leader);
        await ElectSingleNodeLeaderAsync(leader, transport);
        var battle = await ClusterMembershipNode.JoinExistingClusterAsync(
            new NodeId("battle-1"),
            battleEndpoint,
            [leaderEndpoint],
            transport,
            cancellationToken: TestContext.Current.CancellationToken);
        transport.Register(battleEndpoint, battle);
        var gateway = await ClusterMembershipNode.JoinExistingClusterAsync(
            new NodeId("gateway-1"),
            gatewayEndpoint,
            [leaderEndpoint],
            transport,
            cancellationToken: TestContext.Current.CancellationToken);
        transport.Register(gatewayEndpoint, gateway);
        await leader.PromoteLearnerAsync(
            battle.Local,
            transport,
            TestContext.Current.CancellationToken);
        transport.Intercept = (endpoint, request) =>
            endpoint.Address == leaderEndpoint.Address
                && MembershipWireCodec.IsPromoteRequest(request)
                ? MembershipWireCodec.EncodeNotLeaderResponse(thirdEndpoint)
                : null;

        var before = transport.RequestCount;
        var exception = await Assert.ThrowsAsync<AggregateException>(() =>
            gateway.RequestPromotionAsync(
                [battleEndpoint, leaderEndpoint],
                transport,
                TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(2, transport.RequestCount - before);
    }

    [Fact]
    public async Task ReadyFollowsTheNotLeaderHintToTheLeaderOnce()
    {
        var leaderEndpoint = new NodeEndpoint("tcp://data-1:21001");
        var battleEndpoint = new NodeEndpoint("tcp://battle-1:21001");
        var gatewayEndpoint = new NodeEndpoint("tcp://gateway-1:21001");
        var leader = ClusterMembershipNode.BootstrapNewCluster(
            new NodeId("data-1"), leaderEndpoint);
        var transport = new InMemoryMembershipTransport();
        transport.Register(leaderEndpoint, leader);
        await ElectSingleNodeLeaderAsync(leader, transport);
        var battle = await ClusterMembershipNode.JoinExistingClusterAsync(
            new NodeId("battle-1"),
            battleEndpoint,
            [leaderEndpoint],
            transport,
            cancellationToken: TestContext.Current.CancellationToken);
        transport.Register(battleEndpoint, battle);
        var gateway = await ClusterMembershipNode.JoinExistingClusterAsync(
            new NodeId("gateway-1"),
            gatewayEndpoint,
            [leaderEndpoint],
            transport,
            cancellationToken: TestContext.Current.CancellationToken);
        transport.Register(gatewayEndpoint, gateway);
        await leader.PromoteLearnerAsync(
            battle.Local,
            transport,
            TestContext.Current.CancellationToken);
        await gateway.RequestPromotionAsync(
            [battleEndpoint],
            transport,
            TestContext.Current.CancellationToken);

        // The only contact is a non-leader voter that cannot commit ready by
        // itself, so a successful commit proves the client followed the NotLeader
        // hint to the leader exactly once.
        await gateway.RequestReadyAsync(
            [battleEndpoint],
            transport,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ClusterMemberState.Ready,
            Assert.Single(
                gateway.Membership.Current.Members,
                member => member.Reference == gateway.Local).State);
    }

    [Fact]
    public async Task Network_control_round_treats_membership_unavailable_append_as_a_transient_failure()
    {
        var leaderEndpoint = new NodeEndpoint("tcp://data-1:21001");
        var followerEndpoint = new NodeEndpoint("tcp://battle-1:21001");
        var leader = ClusterMembershipNode.BootstrapNewCluster(new NodeId("data-1"), leaderEndpoint);
        var transport = new InMemoryMembershipTransport();
        transport.Register(leaderEndpoint, leader);
        await ElectSingleNodeLeaderAsync(leader, transport);
        var follower = await ClusterMembershipNode.JoinExistingClusterAsync(
            new NodeId("battle-1"), followerEndpoint, [leaderEndpoint], transport,
            cancellationToken: TestContext.Current.CancellationToken);
        transport.Register(followerEndpoint, follower);
        await leader.PromoteLearnerAsync(follower.Local, transport, TestContext.Current.CancellationToken);
        await follower.RequestReadyAsync([leaderEndpoint], transport, TestContext.Current.CancellationToken);
        transport.Intercept = (endpoint, request) =>
            endpoint.Address == followerEndpoint.Address && MembershipWireCodec.IsAppendRequest(request)
                ? MembershipWireCodec.EncodeMembershipUnavailableResponse()
                : null;

        using var cancellation = new CancellationTokenSource();
        var listener = new CancelOnTransientFailureListener(cancellation);
        await leader.RunAsync(listener, transport, cancellation.Token).WaitAsync(
            TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

        var failure = Assert.IsType<InvalidOperationException>(listener.Failure);
        Assert.Contains("could not renew quorum authority", failure.Message, StringComparison.Ordinal);
        Assert.Equal(1, transport.MembershipUnavailableResponseCount);
    }

    private static async Task ElectSingleNodeLeaderAsync(
        ClusterMembershipNode node,
        IClusterMembershipTransport transport)
    {
        // A bootstrapped single-node cluster acquires the leader role only through
        // its authority control loop; the membership protocol no longer elects a
        // leader on ingress. Tests that join into a bootstrapped node must elect it
        // first, matching the hosted-service startup sequence.
        using var cancellation = new CancellationTokenSource();
        var listener = new SharedAuthorityListener(cancellation, expected: 1);
        await node.RunAsync(listener, transport, cancellation.Token).WaitAsync(
            TimeSpan.FromSeconds(3),
            TestContext.Current.CancellationToken);
    }

    private static async Task<ClusterMembershipNode> JoinAndPromoteAsync(
        NodeId node,
        NodeEndpoint endpoint,
        NodeEndpoint leaderEndpoint,
        InMemoryMembershipTransport transport,
        ClusterMembershipNodeOptions? options = null)
    {
        var member = await ClusterMembershipNode.JoinExistingClusterAsync(
            node,
            endpoint,
            [leaderEndpoint],
            transport,
            options,
            cancellationToken: TestContext.Current.CancellationToken);
        transport.Register(endpoint, member);
        await member.RequestPromotionAsync(
            [leaderEndpoint],
            transport,
            TestContext.Current.CancellationToken);
        return member;
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!predicate())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("The membership condition was not reached in time.");
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private sealed class BootstrapListener : IClusterAuthorityListener
    {
        private readonly ClusterMembershipNode node;
        private readonly CancellationTokenSource cancellation;

        public BootstrapListener(
            ClusterMembershipNode node,
            CancellationTokenSource cancellation)
        {
            this.node = node;
            this.cancellation = cancellation;
        }

        public List<MembershipViewId> AvailableViews { get; } = new();

        public int LostCount { get; private set; }

        public ValueTask OnAuthorityAvailableAsync(CancellationToken cancellationToken)
        {
            var snapshot = node.Membership.Current;
            AvailableViews.Add(snapshot.View);
            if (Assert.Single(snapshot.Members).State == ClusterMemberState.Recovering)
            {
                node.CommitLocalReady();
            }
            else
            {
                cancellation.Cancel();
            }

            return default;
        }

        public ValueTask OnAuthorityLostAsync(CancellationToken cancellationToken)
        {
            LostCount++;
            return default;
        }

        public void OnTransientFailure(Exception exception)
        {
            throw new Xunit.Sdk.XunitException(exception.ToString());
        }
    }

    private sealed class InMemoryMembershipTransport : IClusterMembershipTransport
    {
        private readonly Dictionary<string, ClusterMembershipNode> nodes =
            new(StringComparer.Ordinal);

        public string? DropNextEmptyAppendTo { get; set; }

        public string? DeferNextNonEmptyAppendTo { get; set; }

        public bool DeferredAppendStarted { get; private set; }

        public int NonEmptyAppendRequestCount { get; private set; }

        private TaskCompletionSource<ClusterMembershipTransportFrame>? deferredAppend;

        public Func<
            NodeEndpoint,
            ClusterMembershipTransportFrame,
            ClusterMembershipTransportFrame?>? Intercept { get; set; }

        public int RequestCount { get; private set; }

        public int MembershipUnavailableResponseCount { get; private set; }

        public void Register(NodeEndpoint endpoint, ClusterMembershipNode node)
        {
            nodes.Add(endpoint.Address, node);
        }

        public void Unregister(NodeEndpoint endpoint)
        {
            nodes.Remove(endpoint.Address);
        }

        public ValueTask<ClusterMembershipTransportFrame> RequestAsync(
            NodeEndpoint endpoint,
            ClusterMembershipTransportFrame request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCount++;
            if (MembershipWireCodec.IsAppendRequest(request)
                && MembershipWireCodec.DecodeAppendRequest(request).Batch.Entries.Count > 0)
            {
                NonEmptyAppendRequestCount++;
            }
            if (Intercept?.Invoke(endpoint, request) is ClusterMembershipTransportFrame intercepted)
            {
                if (MembershipWireCodec.IsMembershipUnavailableResponse(intercepted))
                {
                    MembershipUnavailableResponseCount++;
                }

                return new ValueTask<ClusterMembershipTransportFrame>(intercepted);
            }

            if (!nodes.TryGetValue(endpoint.Address, out var node))
            {
                throw new IOException("contact unavailable");
            }

            if (string.Equals(DropNextEmptyAppendTo, endpoint.Address, StringComparison.Ordinal)
                && MembershipWireCodec.IsAppendRequest(request)
                && MembershipWireCodec.DecodeAppendRequest(request).Batch.Entries.Count == 0)
            {
                DropNextEmptyAppendTo = null;
                throw new IOException("simulated lost membership commit");
            }

            if (string.Equals(DeferNextNonEmptyAppendTo, endpoint.Address, StringComparison.Ordinal)
                && MembershipWireCodec.IsAppendRequest(request)
                && MembershipWireCodec.DecodeAppendRequest(request).Batch.Entries.Count > 0)
            {
                DeferNextNonEmptyAppendTo = null;
                DeferredAppendStarted = true;
                deferredAppend = new TaskCompletionSource<ClusterMembershipTransportFrame>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                return new ValueTask<ClusterMembershipTransportFrame>(deferredAppend.Task);
            }

            return node.HandleTransportRequestAsync(request, this, cancellationToken);
        }

        public void ReleaseDeferredAppend(ClusterMembershipTransportFrame response)
        {
            if (deferredAppend is null || !deferredAppend.TrySetResult(response))
            {
                throw new InvalidOperationException("No membership append is awaiting release.");
            }
        }
    }

    private sealed class SharedAuthorityListener : IClusterAuthorityListener
    {
        private readonly CancellationTokenSource cancellation;
        private readonly int expected;
        private int available;

        public SharedAuthorityListener(CancellationTokenSource cancellation, int expected)
        {
            this.cancellation = cancellation;
            this.expected = expected;
        }

        public int Available => Volatile.Read(ref available);

        public ValueTask OnAuthorityAvailableAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref available) == expected)
            {
                cancellation.Cancel();
            }

            return default;
        }

        public ValueTask OnAuthorityLostAsync(CancellationToken cancellationToken) => default;

        public void OnTransientFailure(Exception exception)
        {
        }
    }

    private sealed class PassiveAuthorityListener : IClusterAuthorityListener
    {
        private int available;

        public int Available => Volatile.Read(ref available);

        public ValueTask OnAuthorityAvailableAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref available);
            return default;
        }

        public ValueTask OnAuthorityLostAsync(CancellationToken cancellationToken) => default;

        public void OnTransientFailure(Exception exception)
        {
        }
    }

    private sealed class CancelOnTransientFailureListener(CancellationTokenSource cancellation)
        : IClusterAuthorityListener
    {
        public Exception? Failure { get; private set; }

        public ValueTask OnAuthorityAvailableAsync(CancellationToken cancellationToken) => default;

        public ValueTask OnAuthorityLostAsync(CancellationToken cancellationToken) => default;

        public void OnTransientFailure(Exception exception)
        {
            Failure = exception;
            cancellation.Cancel();
        }
    }
}
