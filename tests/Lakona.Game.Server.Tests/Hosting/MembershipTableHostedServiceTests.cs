using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Membership;
using Lakona.Game.Cluster.Rpc.Membership;
using Lakona.Game.Server.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lakona.Game.Server.Hosting;

public sealed class MembershipTableHostedServiceTests
{
    [Fact]
    public async Task ProlongedMembershipTableLossClosesAdmissionAndStopsTheProcess()
    {
        var table = new FailingMembershipTable(new InMemoryMembershipTable());
        var membership = new ClusterMembershipState();
        var runtime = new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "server-1" },
            Cluster = new LakonaGameClusterOptions
            {
                Endpoint = "tcp://127.0.0.1:21001",
                Membership = new LakonaGameMembershipOptions
                {
                    TableRefreshSeconds = 1,
                    IAmAliveSeconds = 2,
                    AllowedIAmAliveMissSeconds = 3,
                    DefunctEntryRetentionSeconds = 4,
                    DefunctEntryCleanupIntervalSeconds = 10
                }
            }
        };
        var manager = new MembershipTableManager(
            runtime.Cluster.Id,
            new NodeId(runtime.Node.Id),
            NodeIncarnationId.New(),
            new NodeEndpoint(runtime.Cluster.Endpoint),
            table,
            membership);
        var gate = new DistributedWorkAdmissionGate();
        var lifetime = new TestApplicationLifetime();
        await using var services = new ServiceCollection().BuildServiceProvider();
        var hosted = new MembershipTableHostedService(
            runtime,
            manager,
            membership,
            new SingleNodeProbeTransport(),
            gate,
            [],
            services,
            NullLogger<MembershipTableHostedService>.Instance,
            lifetime);

        await hosted.StartAsync(TestContext.Current.CancellationToken);
        Assert.True(gate.IsOpen);
        table.Fail = true;

        await lifetime.Stopped.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        Assert.False(gate.IsOpen);
        await hosted.StopAsync(TestContext.Current.CancellationToken);
    }

    private sealed class FailingMembershipTable(IMembershipTable inner) : IMembershipTable
    {
        public bool Fail { get; set; }

        public ValueTask<MembershipTableGeneration> AllocateGenerationAsync(string clusterId, CancellationToken cancellationToken = default) =>
            Invoke(() => inner.AllocateGenerationAsync(clusterId, cancellationToken));

        public ValueTask<MembershipTableSnapshot> ReadOrCreateAsync(string clusterId, CancellationToken cancellationToken = default) =>
            Invoke(() => inner.ReadOrCreateAsync(clusterId, cancellationToken));

        public ValueTask<bool> TryInsertAsync(string clusterId, MembershipTableEntry entry, MembershipViewId expectedVersion, CancellationToken cancellationToken = default) =>
            Invoke(() => inner.TryInsertAsync(clusterId, entry, expectedVersion, cancellationToken));

        public ValueTask<bool> TryReplaceAsync(string clusterId, NodeReference previous, long expectedPreviousVersion, MembershipTableEntry replacement, MembershipViewId expectedVersion, CancellationToken cancellationToken = default) =>
            Invoke(() => inner.TryReplaceAsync(clusterId, previous, expectedPreviousVersion, replacement, expectedVersion, cancellationToken));

        public ValueTask<bool> TryUpdateAsync(string clusterId, MembershipTableEntry entry, long expectedEntryVersion, MembershipViewId expectedVersion, CancellationToken cancellationToken = default) =>
            Invoke(() => inner.TryUpdateAsync(clusterId, entry, expectedEntryVersion, expectedVersion, cancellationToken));

        public ValueTask<bool> TryUpdateIAmAliveAsync(string clusterId, NodeReference reference, DateTimeOffset timestamp, CancellationToken cancellationToken = default) =>
            Invoke(() => inner.TryUpdateIAmAliveAsync(clusterId, reference, timestamp, cancellationToken));

        public ValueTask<int> CleanupDefunctAsync(string clusterId, DateTimeOffset before, int maximumRows, CancellationToken cancellationToken = default) =>
            Invoke(() => inner.CleanupDefunctAsync(clusterId, before, maximumRows, cancellationToken));

        private ValueTask<T> Invoke<T>(Func<ValueTask<T>> action)
        {
            if (Fail) throw new InvalidOperationException("Membership Table unavailable.");
            return action();
        }
    }

    private sealed class SingleNodeProbeTransport : IMembershipProbeTransport
    {
        public ValueTask<bool> ProbeAsync(NodeReference source, ClusterMember target, NodeEndpoint contact, bool forward, CancellationToken cancellationToken = default) =>
            new(false);

        public ValueTask GossipAsync(NodeReference source, NodeEndpoint contact, MembershipViewId version, CancellationToken cancellationToken = default) =>
            default;
    }

    private sealed class TestApplicationLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource stopping = new();
        public TaskCompletionSource Stopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => stopping.Token;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication()
        {
            stopping.Cancel();
            Stopped.TrySetResult();
        }
    }
}
