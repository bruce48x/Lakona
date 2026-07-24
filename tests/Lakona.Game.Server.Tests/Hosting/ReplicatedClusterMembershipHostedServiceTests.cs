using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc.Membership;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Hosting;
using Xunit;

namespace Lakona.Game.Server.Tests.Hosting;

public sealed class ReplicatedClusterMembershipHostedServiceTests
{
    [Fact]
    public async Task JoiningNodeRetriesDiscoveryWithoutBootstrappingAnotherCluster()
    {
        var leaderEndpoint = new NodeEndpoint("tcp://127.0.0.1:21001");
        var leader = ClusterMembershipNode.BootstrapNewCluster(
            new NodeId("data-1"),
            leaderEndpoint);
        var transport = new EventuallyAvailableTransport(leader, failuresBeforeReady: 2);
        var service = new ReplicatedClusterMembershipHostedService(
            new LakonaGameRuntimeOptions
            {
                Node = new LakonaGameNodeOptions { Id = "gateway-1" },
                Cluster = new LakonaGameClusterOptions
                {
                    Endpoint = "tcp://127.0.0.1:21002",
                    Seeds = [leaderEndpoint.Address]
                }
            },
            new DistributedWorkAdmissionGate(),
            Array.Empty<IClusterRecoveryParticipant>(),
            transport,
            new ClusterMembershipNodeOptions
            {
                MinimumRetryDelay = TimeSpan.FromMilliseconds(1),
                MaximumRetryDelay = TimeSpan.FromMilliseconds(2),
                JoinRetryWindow = TimeSpan.FromSeconds(1)
            });

        await service.StartAsync(TestContext.Current.CancellationToken);
        // Joining returns before learner promotion; observe that later request so
        // the discovery assertion cannot depend on background-task scheduling.
        await transport.WaitForPostJoinRequestAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(3, transport.DiscoveryRequestCount);
        Assert.Equal("gateway-1", ((IClusterMembership)service).Current.Members
            .Single(member => member.Reference.Node.Value == "gateway-1")
            .Reference.Node.Value);

        await service.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ExplicitSingleNodeBootstrapStartsReadyAndStopsFenced()
    {
        var gate = new DistributedWorkAdmissionGate();
        var service = new ReplicatedClusterMembershipHostedService(
            new LakonaGameRuntimeOptions
            {
                Node = new LakonaGameNodeOptions { Id = "data-1" },
                Cluster = new LakonaGameClusterOptions
                {
                    Endpoint = "tcp://127.0.0.1:21001",
                    BootstrapNewCluster = true
                }
            },
            gate,
            Array.Empty<IClusterRecoveryParticipant>(),
            new ClusterMembershipNodeOptions
            {
                HeartbeatInterval = TimeSpan.FromMilliseconds(1),
                ProofValidity = TimeSpan.FromSeconds(1),
                MinimumRetryDelay = TimeSpan.FromMilliseconds(1),
                MaximumRetryDelay = TimeSpan.FromMilliseconds(10)
            });

        await service.StartAsync(TestContext.Current.CancellationToken);

        Assert.True(gate.IsOpen);
        Assert.Equal(
            ClusterMemberState.Ready,
            Assert.Single(((IClusterMembership)service).Current.Members).State);

        await service.StopAsync(TestContext.Current.CancellationToken);
        Assert.False(gate.IsOpen);
    }

    private sealed class EventuallyAvailableTransport : IClusterMembershipTransport
    {
        private readonly ClusterMembershipNode leader;
        private readonly TaskCompletionSource postJoinRequestObserved = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int remainingFailures;
        private bool joinCompleted;

        public EventuallyAvailableTransport(
            ClusterMembershipNode leader,
            int failuresBeforeReady)
        {
            this.leader = leader;
            remainingFailures = failuresBeforeReady;
        }

        public int DiscoveryRequestCount { get; private set; }

        public Task WaitForPostJoinRequestAsync(CancellationToken cancellationToken)
        {
            return postJoinRequestObserved.Task.WaitAsync(cancellationToken);
        }

        public async ValueTask<ClusterMembershipTransportFrame> RequestAsync(
            NodeEndpoint endpoint,
            ClusterMembershipTransportFrame request,
            CancellationToken cancellationToken = default)
        {
            if (joinCompleted)
            {
                postJoinRequestObserved.TrySetResult();
                return await leader
                    .HandleTransportRequestAsync(request, null, cancellationToken)
                    .ConfigureAwait(false);
            }

            DiscoveryRequestCount++;
            if (remainingFailures-- > 0)
            {
                throw new IOException("The discovery listener is not ready yet.");
            }

            var response = await leader
                .HandleTransportRequestAsync(request, null, cancellationToken)
                .ConfigureAwait(false);
            joinCompleted = true;
            return response;
        }
    }
}
