using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc.Membership;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Hosting;
using Microsoft.Extensions.Logging;
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
        // A bootstrapped single-node cluster acquires the leader role through its
        // authority control loop, not on ingress; elect the contact before the
        // joining service forms, matching the hosted-service startup sequence.
        using var leaderCancellation = new CancellationTokenSource();
        var leaderLoop = leader.RunAsync(
            new NoopAuthorityListener(),
            transport,
            leaderCancellation.Token);
        await ClusterTestWait.UntilAsync(() => leader.IsLeader, TimeSpan.FromSeconds(2));
        var joiningEndpoint = new NodeEndpoint("tcp://127.0.0.1:21002");
        var membership = new ClusterMembershipState();
        var service = new ReplicatedClusterMembershipHostedService(
            new LakonaGameRuntimeOptions
            {
                Node = new LakonaGameNodeOptions { Id = "gateway-1" },
                Cluster = new LakonaGameClusterOptions
                {
                    Endpoint = joiningEndpoint.Address,
                    Peers =
                    [
                        new LakonaGameClusterPeerOptions
                        {
                            Id = "data-1",
                            Endpoint = leaderEndpoint.Address
                        }
                    ]
                }
            },
            new DistributedWorkAdmissionGate(),
            Array.Empty<IClusterRecoveryParticipant>(),
            transport,
            membership,
            new ClusterMembershipNodeOptions
            {
                MinimumRetryDelay = TimeSpan.FromMilliseconds(1),
                MaximumRetryDelay = TimeSpan.FromMilliseconds(2),
                JoinRetryWindow = TimeSpan.FromSeconds(1)
            });
        transport.Register(joiningEndpoint, service.HandleAsync);

        using var startupCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var startup = service.StartAsync(startupCancellation.Token);
        await transport.WaitForPostJoinRequestAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(3, transport.DiscoveryRequestCount);
        Assert.Equal("gateway-1", membership.Current.Members
            .Single(member => member.Reference.Node.Value == "gateway-1")
            .Reference.Node.Value);

        await startupCancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => startup);
        await service.StopAsync(TestContext.Current.CancellationToken);
        await leaderCancellation.CancelAsync();
        await leaderLoop;
    }

    [Fact]
    public async Task SingleNodeAutomaticallyFormsReadyClusterAndStopsFenced()
    {
        var gate = new DistributedWorkAdmissionGate();
        var membership = new ClusterMembershipState();
        var service = new ReplicatedClusterMembershipHostedService(
            new LakonaGameRuntimeOptions
            {
                Node = new LakonaGameNodeOptions { Id = "data-1" },
                Cluster = new LakonaGameClusterOptions
                {
                    Endpoint = "tcp://127.0.0.1:21001"
                }
            },
            gate,
            Array.Empty<IClusterRecoveryParticipant>(),
            new UnexpectedMembershipTransport(),
            membership,
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
            Assert.Single(membership.Current.Members).State);

        await service.StopAsync(TestContext.Current.CancellationToken);
        Assert.False(gate.IsOpen);
    }

    [Fact]
    public async Task TransientFailuresLogTheFirstExceptionAtDebugAndRepeatsAtTrace()
    {
        var logger = new RecordingLogger<ReplicatedClusterMembershipHostedService>();
        var service = new ReplicatedClusterMembershipHostedService(
            new LakonaGameRuntimeOptions
            {
                Node = new LakonaGameNodeOptions { Id = "data-1" },
                Cluster = new LakonaGameClusterOptions
                {
                    Endpoint = "tcp://127.0.0.1:21001"
                }
            },
            new DistributedWorkAdmissionGate(),
            Array.Empty<IClusterRecoveryParticipant>(),
            new UnexpectedMembershipTransport(),
            new ClusterMembershipState(),
            new ClusterMembershipNodeOptions
            {
                HeartbeatInterval = TimeSpan.FromMilliseconds(10),
                ProofValidity = TimeSpan.FromSeconds(1)
            },
            services: null,
            logger: logger);

        await service.StartAsync(TestContext.Current.CancellationToken);
        var first = new InvalidOperationException("first failure");
        var repeated = new IOException("second failure");

        service.OnTransientFailure(first);
        service.OnTransientFailure(repeated);

        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Debug && ReferenceEquals(entry.Exception, first));
        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Trace
            && entry.Message.Contains("second failure", StringComparison.Ordinal));

        await service.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task MarkUnavailablePermanentlyFencesDistributedWorkForThisProcess()
    {
        var gate = new DistributedWorkAdmissionGate();
        var service = new ReplicatedClusterMembershipHostedService(
            new LakonaGameRuntimeOptions
            {
                Node = new LakonaGameNodeOptions { Id = "data-1" },
                Cluster = new LakonaGameClusterOptions
                {
                    Endpoint = "tcp://127.0.0.1:21001"
                }
            },
            gate,
            Array.Empty<IClusterRecoveryParticipant>(),
            new UnexpectedMembershipTransport(),
            new ClusterMembershipState(),
            new ClusterMembershipNodeOptions
            {
                HeartbeatInterval = TimeSpan.FromSeconds(10),
                ProofValidity = TimeSpan.FromSeconds(30)
            });
        var refresher = new ClusterMembershipDescriptorRefresher(service);

        await service.StartAsync(TestContext.Current.CancellationToken);
        Assert.True(gate.IsOpen);

        await refresher.MarkUnavailableAsync();

        Assert.False(gate.IsOpen);
        await service.OnAuthorityAvailableAsync(TestContext.Current.CancellationToken);
        Assert.False(gate.IsOpen);
        await Assert.ThrowsAsync<ClusterAuthorityFencingException>(async () =>
            await refresher.RefreshAsync(TestContext.Current.CancellationToken));

        await service.StopAsync(TestContext.Current.CancellationToken);
    }

    private sealed class NoopAuthorityListener : IClusterAuthorityListener
    {
        public ValueTask OnAuthorityAvailableAsync(CancellationToken cancellationToken) => default;

        public ValueTask OnAuthorityLostAsync(CancellationToken cancellationToken) => default;

        public void OnTransientFailure(Exception exception)
        {
        }
    }

    private sealed class UnexpectedMembershipTransport : IClusterMembershipTransport
    {
        public ValueTask<ClusterMembershipTransportFrame> RequestAsync(
            NodeEndpoint endpoint,
            ClusterMembershipTransportFrame request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException(
                "A single-node hosted-service test unexpectedly used the membership transport.");
        }
    }

    private sealed class EventuallyAvailableTransport : IClusterMembershipTransport
    {
        private readonly ClusterMembershipNode leader;
        private readonly TaskCompletionSource postJoinRequestObserved = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int remainingFailures;
        private bool joinCompleted;
        private NodeEndpoint? joiningEndpoint;
        private Func<
            ClusterMembershipTransportFrame,
            CancellationToken,
            ValueTask<ClusterMembershipTransportFrame>>? joiningHandler;

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

        public void Register(
            NodeEndpoint endpoint,
            Func<
                ClusterMembershipTransportFrame,
                CancellationToken,
                ValueTask<ClusterMembershipTransportFrame>> handler)
        {
            joiningEndpoint = endpoint;
            joiningHandler = handler;
        }

        public async ValueTask<ClusterMembershipTransportFrame> RequestAsync(
            NodeEndpoint endpoint,
            ClusterMembershipTransportFrame request,
            CancellationToken cancellationToken = default)
        {
            if (MembershipWireCodec.IsFormationProbeRequest(request))
            {
                DiscoveryRequestCount++;
                if (remainingFailures-- > 0)
                {
                    throw new IOException("The discovery listener is not ready yet.");
                }

                return MembershipWireCodec.EncodeFormationProbeResponse(
                    established: true,
                    [
                        new ClusterFormationPeer(
                            leader.Local.Node,
                            leader.Membership.Current.Members[0].ClusterEndpoint)
                    ]);
            }

            if (joiningEndpoint == endpoint && joiningHandler is not null)
            {
                postJoinRequestObserved.TrySetResult();
                return await joiningHandler(request, cancellationToken).ConfigureAwait(false);
            }

            if (joinCompleted)
            {
                postJoinRequestObserved.TrySetResult();
                return await leader
                    .HandleTransportRequestAsync(request, null, cancellationToken)
                    .ConfigureAwait(false);
            }

            var response = await leader
                .HandleTransportRequestAsync(request, null, cancellationToken)
                .ConfigureAwait(false);
            joinCompleted = true;
            return response;
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);
}
