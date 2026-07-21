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

        Assert.Equal(3, transport.RequestCount);
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
        private int remainingFailures;

        public EventuallyAvailableTransport(
            ClusterMembershipNode leader,
            int failuresBeforeReady)
        {
            this.leader = leader;
            remainingFailures = failuresBeforeReady;
        }

        public int RequestCount { get; private set; }

        public ValueTask<ClusterMembershipTransportFrame> RequestAsync(
            NodeEndpoint endpoint,
            ClusterMembershipTransportFrame request,
            CancellationToken cancellationToken = default)
        {
            RequestCount++;
            if (remainingFailures-- > 0)
            {
                throw new IOException("The discovery listener is not ready yet.");
            }

            return leader.HandleTransportRequestAsync(request, null, cancellationToken);
        }
    }
}
