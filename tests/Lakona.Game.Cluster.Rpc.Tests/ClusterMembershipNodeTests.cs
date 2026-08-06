using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc.Membership;
using Xunit;

namespace Lakona.Game.Cluster.Rpc.Tests;

public sealed class ClusterMembershipNodeTests
{
    [Fact]
    public async Task Ready_request_skips_membership_unavailable_contact_and_uses_next_contact()
    {
        var node = ClusterMembershipNode.BootstrapNewCluster(
            new NodeId("data-1"), new NodeEndpoint("tcp://data-1:21001"));
        node.CommitLocalReady();
        var first = new NodeEndpoint("tcp://first:21001");
        var second = new NodeEndpoint("tcp://second:21001");
        var transport = new ScriptedMembershipTransport(first, second,
            MembershipWireCodec.EncodeReadyResponse(node.Membership.Current));

        var snapshot = await node.RequestReadyAsync([first, second], transport, TestContext.Current.CancellationToken);

        Assert.Equal(node.Membership.Current.View, snapshot.View);
        Assert.Equal(new[] { first.Address, second.Address }, transport.RequestedAddresses);
    }

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

        public Func<
            NodeEndpoint,
            ClusterMembershipTransportFrame,
            ClusterMembershipTransportFrame?>? Intercept { get; set; }

        public int RequestCount { get; private set; }

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
            if (Intercept?.Invoke(endpoint, request) is ClusterMembershipTransportFrame intercepted)
            {
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

            return node.HandleTransportRequestAsync(request, this, cancellationToken);
        }
    }

    private sealed class ScriptedMembershipTransport(
        NodeEndpoint unavailable,
        NodeEndpoint available,
        ClusterMembershipTransportFrame response) : IClusterMembershipTransport
    {
        public List<string> RequestedAddresses { get; } = [];

        public ValueTask<ClusterMembershipTransportFrame> RequestAsync(
            NodeEndpoint endpoint,
            ClusterMembershipTransportFrame request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedAddresses.Add(endpoint.Address);
            if (endpoint.Address == unavailable.Address)
            {
                return ValueTask.FromResult(MembershipWireCodec.EncodeMembershipUnavailableResponse());
            }

            if (endpoint.Address == available.Address)
            {
                return ValueTask.FromResult(response);
            }

            throw new IOException("Unexpected contact.");
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
}
