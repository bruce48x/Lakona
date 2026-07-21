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
