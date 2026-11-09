using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Membership;
using Lakona.Game.Cluster.Rpc;
using System.Net;
using System.Net.Sockets;
using Lakona.Game.Cluster.Rpc.Membership;
using Lakona.Game.Server.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lakona.Game.Server.Hosting;

public sealed class MembershipTableHostedServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stop_handles_cancellation_before_background_execution_starts(bool expiredStopDeadline)
    {
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var table = new InMemoryMembershipTable();
        var state = new ClusterMembershipState();
        var manager = new MembershipTableManager(new NodeId("worker"), NodeIncarnationId.New(),
            new NodeEndpoint("tcp://127.0.0.1:21001"), new ClusterBuildTag("TestBuild1"), table, state);
        using var services = new ServiceCollection().BuildServiceProvider();
        using var hosted = new MembershipTableHostedService(new LakonaGameRuntimeOptions(), manager,
            state, new SingleNodeProbeTransport(), new DistributedWorkAdmissionGate(), [], services,
            NullLogger<MembershipTableHostedService>.Instance,
            timeProvider: new CancelOnSafetyStartTimeProvider(startup));

        // Cancel at the safety-window start, immediately before BackgroundService
        // schedules ExecuteAsync. Its task is canceled without running any worker.
        await hosted.StartAsync(startup.Token);
        Assert.True(hosted.ExecuteTask!.IsCanceled);
        if (expiredStopDeadline)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => hosted.StopAsync(startup.Token));
            Assert.NotEqual(MembershipTableStatus.Dead,
                Assert.Single((await table.ReadOrCreateAsync(TestContext.Current.CancellationToken)).Entries).Status);
        }
        await hosted.StopAsync(TestContext.Current.CancellationToken);
        Assert.Equal(MembershipTableStatus.Dead,
            Assert.Single((await table.ReadOrCreateAsync(TestContext.Current.CancellationToken)).Entries).Status);
    }

    private sealed class CancelOnSafetyStartTimeProvider(CancellationTokenSource startup) : TimeProvider
    {
        public override long GetTimestamp()
        {
            startup.Cancel();
            return base.GetTimestamp();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Allocation_failure_ends_startup_without_retrying(bool committed)
    {
        var inner = new InMemoryMembershipTable();
        var failure = new IOException("Allocation response unavailable.");
        var table = new FailingMembershipTable(inner);
        if (committed) table.AfterAllocate = () => throw failure;
        else table.AllocationError = failure;
        var state = new ClusterMembershipState();
        var manager = new MembershipTableManager(new NodeId("worker"), NodeIncarnationId.New(),
            new NodeEndpoint("tcp://127.0.0.1:21001"), new ClusterBuildTag("TestBuild1"), table, state);
        var probes = new RecordingProbeTransport((_, _, _) => true);
        using var services = new ServiceCollection().BuildServiceProvider();
        using var hosted = new MembershipTableHostedService(new LakonaGameRuntimeOptions(), manager,
            state, probes, new DistributedWorkAdmissionGate(), [], services,
            NullLogger<MembershipTableHostedService>.Instance);

        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => hosted.StartAsync(TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)));
        Assert.Equal(1, table.AllocateAttempts);
        Assert.Empty(probes.Calls);
        Assert.Empty((await inner.ReadOrCreateAsync(TestContext.Current.CancellationToken)).Entries);
        Assert.Throws<InvalidOperationException>(() => manager.Local);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Startup_read_retry_keeps_its_generation_and_respects_a_newer_process(bool replaced)
    {
        var ct = TestContext.Current.CancellationToken;
        var inner = new InMemoryMembershipTable();
        var table = new FailingMembershipTable(inner);
        var state = new ClusterMembershipState();
        var manager = new MembershipTableManager(new NodeId("worker"), NodeIncarnationId.New(),
            new NodeEndpoint("tcp://127.0.0.1:21001"), new ClusterBuildTag("TestBuild1"), table, state);
        NodeReference? successor = null;
        table.BeforeRead = async token =>
        {
            table.BeforeRead = null;
            if (replaced)
            {
                var newer = new MembershipTableManager(new NodeId("worker"), NodeIncarnationId.New(),
                    new NodeEndpoint("tcp://127.0.0.1:21001"), new ClusterBuildTag("TestBuild1"),
                    inner, new ClusterMembershipState());
                successor = await newer.JoinAsync(token);
                await newer.ActivateAsync(null, [], [], token);
            }
            throw new IOException("Read failed after generation allocation.");
        };
        using var services = new ServiceCollection().BuildServiceProvider();
        using var hosted = new MembershipTableHostedService(new LakonaGameRuntimeOptions(), manager,
            state, new SingleNodeProbeTransport(), new DistributedWorkAdmissionGate(), [], services,
            NullLogger<MembershipTableHostedService>.Instance);
        try
        {
            if (replaced)
                await Assert.ThrowsAsync<ClusterMembershipFencedException>(() => hosted.StartAsync(ct)
                    .WaitAsync(TimeSpan.FromSeconds(10), ct));
            else await hosted.StartAsync(ct).WaitAsync(TimeSpan.FromSeconds(10), ct);

            Assert.Equal(1, table.AllocateAttempts);
            var current = Assert.Single((await inner.ReadOrCreateAsync(ct)).Entries);
            Assert.Equal(replaced ? successor : manager.Local, current.Reference);
            Assert.Equal(replaced ? 2 : 1, current.Generation);
            Assert.Equal(MembershipTableStatus.Active, current.Status);
            if (replaced) Assert.Throws<InvalidOperationException>(() => state.Current);
        }
        finally
        {
            if (!replaced) await hosted.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Real_refused_direct_probes_reach_death_threshold_in_two_node_cluster()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var endpoint = new NodeEndpoint($"tcp://127.0.0.1:{((IPEndPoint)socket.LocalEndPoint!).Port}");
        var cluster = await CreateActiveClusterAsync(2, failedProbesBeforeSuspect: 2, peerEndpoint: endpoint);
        await using var clients = new ClusterClientFactory(new ClusterRpcChannel());
        var probes = new RpcMembershipProbeTransport(clients, TimeSpan.FromSeconds(5));
        await using var services = new ServiceCollection().BuildServiceProvider();
        var hosted = CreateHosted(cluster.Runtime, cluster.Managers[0], cluster.States[0], cluster.Table, probes, services);
        await hosted.RunProbeCycleAsync(cluster.Runtime.Cluster.Membership, TestContext.Current.CancellationToken);
        Assert.All((await cluster.Table.ReadOrCreateAsync(TestContext.Current.CancellationToken)).Entries,
            row => Assert.Equal(MembershipTableStatus.Active, row.Status));
        await hosted.RunProbeCycleAsync(cluster.Runtime.Cluster.Membership, TestContext.Current.CancellationToken);
        var target = (await cluster.Table.ReadOrCreateAsync(TestContext.Current.CancellationToken)).Entries.Single(e => e.Reference == cluster.Managers[1].Local);
        Assert.Equal(MembershipTableStatus.Dead, target.Status);
    }

    [Fact]
    public async Task Unknown_helpers_are_rotated_and_direct_unknown_never_counts()
    {
        var cluster = await CreateActiveClusterAsync(5, failedProbesBeforeSuspect: 1);
        var target = cluster.Managers[1].Local;
        var probes = new RecordingProbeTransport((member, _, forward) => member.Reference != target
            ? MembershipProbeStatus.Unknown
            : forward ? MembershipProbeStatus.Unknown : MembershipProbeStatus.Failed);
        await using var services = new ServiceCollection().BuildServiceProvider();
        var hosted = CreateHosted(cluster.Runtime, cluster.Managers[0], cluster.States[0], cluster.Table, probes, services);
        for (var i = 0; i < 3; i++)
            await hosted.RunProbeCycleAsync(cluster.Runtime.Cluster.Membership, TestContext.Current.CancellationToken);
        var helpers = probes.Calls.Where(c => c.Forward && c.Target == target).Select(c => c.Contact).Distinct();
        Assert.Equal(3, helpers.Count());
        Assert.All((await cluster.Table.ReadOrCreateAsync(TestContext.Current.CancellationToken)).Entries,
            row => { Assert.Equal(MembershipTableStatus.Active, row.Status); Assert.Empty(row.SuspectVotes); });
    }

    [Fact]
    public async Task Unknown_helpers_neither_create_nor_refresh_suspicion_votes()
    {
        var cluster = await CreateActiveClusterAsync(3, failedProbesBeforeSuspect: 1, votesForDeath: 2);
        var target = cluster.Managers[1].Local;
        var helperStatus = MembershipProbeStatus.Unknown;
        var probes = new RecordingProbeTransport((member, _, forward) => member.Reference != target
            ? MembershipProbeStatus.Succeeded
            : forward ? helperStatus : MembershipProbeStatus.Failed);
        await using var services = new ServiceCollection().BuildServiceProvider();
        var hosted = CreateHosted(cluster.Runtime, cluster.Managers[0], cluster.States[0], cluster.Table, probes, services);
        for (var i = 0; i < 4; i++)
            await hosted.RunProbeCycleAsync(cluster.Runtime.Cluster.Membership, TestContext.Current.CancellationToken);
        var row = (await cluster.Table.ReadOrCreateAsync(TestContext.Current.CancellationToken)).Entries.Single(e => e.Reference == target);
        Assert.Empty(row.SuspectVotes);
        helperStatus = MembershipProbeStatus.Failed;
        await hosted.RunProbeCycleAsync(cluster.Runtime.Cluster.Membership, TestContext.Current.CancellationToken);
        row = (await cluster.Table.ReadOrCreateAsync(TestContext.Current.CancellationToken)).Entries.Single(e => e.Reference == target);
        Assert.Single(row.SuspectVotes);
        helperStatus = MembershipProbeStatus.Unknown;
        await hosted.RunProbeCycleAsync(cluster.Runtime.Cluster.Membership, TestContext.Current.CancellationToken);
        var unchanged = (await cluster.Table.ReadOrCreateAsync(TestContext.Current.CancellationToken)).Entries.Single(e => e.Reference == target);
        Assert.Equal(row.Version, unchanged.Version);
        Assert.Equal(row.SuspectVotes, unchanged.SuspectVotes);
        Assert.Equal(MembershipTableStatus.Active, unchanged.Status);
    }

    [Theory]
    [InlineData(0)] // Unknown preserves previous failures.
    [InlineData(1)] // Success clears previous failures, even after another helper failed.
    public async Task Helper_results_are_aggregated_once_per_round(int middleStatus)
    {
        var cluster = await CreateActiveClusterAsync(4, failedProbesBeforeSuspect: 2);
        var target = cluster.Managers[1].Local;
        var round = 0;
        var probes = new RecordingProbeTransport((member, contact, forward) =>
        {
            if (member.Reference != target) return MembershipProbeStatus.Succeeded;
            if (!forward) return MembershipProbeStatus.Failed;
            if (round == 1) return (MembershipProbeStatus)middleStatus;
            return contact == cluster.States[2].Current.Members.Single(m => m.Reference == cluster.Managers[2].Local).ClusterEndpoint
                ? MembershipProbeStatus.Failed : MembershipProbeStatus.Unknown;
        });
        await using var services = new ServiceCollection().BuildServiceProvider();
        var hosted = CreateHosted(cluster.Runtime, cluster.Managers[0], cluster.States[0], cluster.Table, probes, services);
        for (; round < 3; round++)
            await hosted.RunProbeCycleAsync(cluster.Runtime.Cluster.Membership, TestContext.Current.CancellationToken);
        var row = (await cluster.Table.ReadOrCreateAsync(TestContext.Current.CancellationToken)).Entries.Single(e => e.Reference == target);
        Assert.Equal(middleStatus == 0 ? MembershipTableStatus.Dead : MembershipTableStatus.Active, row.Status);
    }

    [Fact]
    public async Task Later_helper_success_overrides_an_earlier_failure()
    {
        var cluster = await CreateActiveClusterAsync(4, failedProbesBeforeSuspect: 1);
        var calls = 0;
        var probes = new RecordingProbeTransport((_, _, forward) => !forward || ++calls % 2 == 1
            ? MembershipProbeStatus.Failed : MembershipProbeStatus.Succeeded);
        await using var services = new ServiceCollection().BuildServiceProvider();
        var hosted = CreateHosted(cluster.Runtime, cluster.Managers[0], cluster.States[0], cluster.Table, probes, services);
        await hosted.RunProbeCycleAsync(cluster.Runtime.Cluster.Membership, TestContext.Current.CancellationToken);
        Assert.All((await cluster.Table.ReadOrCreateAsync(TestContext.Current.CancellationToken)).Entries,
            row => { Assert.Equal(MembershipTableStatus.Active, row.Status); Assert.Empty(row.SuspectVotes); });
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 2)]
    public async Task Startup_does_not_evict_stale_peer_without_failed_direct_probe(int direct, int reverse)
    {
        var cluster = await CreateJoiningNodeAgainstActivePeerAsync(TimeSpan.FromMinutes(11));
        await using var services = new ServiceCollection().BuildServiceProvider();
        var probes = new RecordingProbeTransport((_, _, forward) => (MembershipProbeStatus)(forward ? reverse : direct));
        var hosted = CreateHosted(cluster.Runtime, cluster.Joining, cluster.State, cluster.Table, probes, services);
        await Assert.ThrowsAsync<InvalidOperationException>(() => hosted.ValidateStartupConnectivityAsync(TestContext.Current.CancellationToken).AsTask());
        var row = (await cluster.Table.ReadOrCreateAsync(TestContext.Current.CancellationToken)).Entries.Single(e => e.Reference == cluster.Active);
        Assert.Equal(MembershipTableStatus.Active, row.Status);
    }

    [Fact]
    public async Task IndirectProbeSuccessPreventsSuspicionAfterDirectProbeFailure()
    {
        var cluster = await CreateActiveClusterAsync(3);
        var probes = new RecordingProbeTransport((_, _, forward) => forward);
        await using var services = new ServiceCollection().BuildServiceProvider();
        var hosted = CreateHosted(cluster.Runtime, cluster.Managers[0], cluster.States[0], cluster.Table, probes, services);

        await hosted.RunProbeCycleAsync(cluster.Runtime.Cluster.Membership, TestContext.Current.CancellationToken);

        var snapshot = await cluster.Table.ReadOrCreateAsync(TestContext.Current.CancellationToken);
        Assert.All(snapshot.Entries, static entry => Assert.Equal(MembershipTableStatus.Active, entry.Status));
        Assert.Contains(probes.Calls, static call => call.Forward);
    }

    [Fact]
    public async Task SuccessfulProbeResetsConsecutiveFailureThreshold()
    {
        var cluster = await CreateActiveClusterAsync(2, failedProbesBeforeSuspect: 2);
        var directSuccess = false;
        var probes = new RecordingProbeTransport((_, _, _) => directSuccess);
        await using var services = new ServiceCollection().BuildServiceProvider();
        var hosted = CreateHosted(cluster.Runtime, cluster.Managers[0], cluster.States[0], cluster.Table, probes, services);
        var options = cluster.Runtime.Cluster.Membership;

        await hosted.RunProbeCycleAsync(options, TestContext.Current.CancellationToken);
        directSuccess = true;
        await hosted.RunProbeCycleAsync(options, TestContext.Current.CancellationToken);
        directSuccess = false;
        await hosted.RunProbeCycleAsync(options, TestContext.Current.CancellationToken);

        var snapshot = await cluster.Table.ReadOrCreateAsync(TestContext.Current.CancellationToken);
        Assert.All(snapshot.Entries, static entry => Assert.Equal(MembershipTableStatus.Active, entry.Status));
    }

    [Fact]
    public async Task ConsecutiveProbeFailuresDeclareTargetDeadAtThreshold()
    {
        var cluster = await CreateActiveClusterAsync(2, failedProbesBeforeSuspect: 2);
        var probes = new RecordingProbeTransport((_, _, _) => false);
        await using var services = new ServiceCollection().BuildServiceProvider();
        var hosted = CreateHosted(cluster.Runtime, cluster.Managers[0], cluster.States[0], cluster.Table, probes, services);

        await hosted.RunProbeCycleAsync(cluster.Runtime.Cluster.Membership, TestContext.Current.CancellationToken);
        await hosted.RunProbeCycleAsync(cluster.Runtime.Cluster.Membership, TestContext.Current.CancellationToken);

        var snapshot = await cluster.Table.ReadOrCreateAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            MembershipTableStatus.Dead,
            snapshot.Entries.Single(entry => entry.Reference == cluster.Managers[1].Local).Status);
    }

    [Fact]
    public async Task ProbeFailuresFromOldIncarnationDoNotCountAgainstReplacement()
    {
        var cluster = await CreateActiveClusterAsync(2, failedProbesBeforeSuspect: 2);
        var probes = new RecordingProbeTransport((_, _, _) => false);
        await using var services = new ServiceCollection().BuildServiceProvider();
        var hosted = CreateHosted(cluster.Runtime, cluster.Managers[0], cluster.States[0], cluster.Table, probes, services);
        await hosted.RunProbeCycleAsync(cluster.Runtime.Cluster.Membership, TestContext.Current.CancellationToken);
        var replacementState = new ClusterMembershipState();
        var replacement = new MembershipTableManager(
            new NodeId("server-2"),
            NodeIncarnationId.New(),
            new NodeEndpoint("tcp://127.0.0.1:21002"),
            new ClusterBuildTag("TestBuild1"),
            cluster.Table,
            replacementState);
        await replacement.JoinAsync(TestContext.Current.CancellationToken);
        await replacement.ActivateAsync(null, [], [], TestContext.Current.CancellationToken);
        await cluster.Managers[0].RefreshAsync(TestContext.Current.CancellationToken);

        await hosted.RunProbeCycleAsync(cluster.Runtime.Cluster.Membership, TestContext.Current.CancellationToken);

        var snapshot = await cluster.Table.ReadOrCreateAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            MembershipTableStatus.Active,
            snapshot.Entries.Single(entry => entry.Reference == replacement.Local).Status);
    }

    [Fact]
    public async Task StartupConnectivityFailsClosedForFreshUnreachableMember()
    {
        var cluster = await CreateJoiningNodeAgainstActivePeerAsync(TimeSpan.Zero);
        await using var services = new ServiceCollection().BuildServiceProvider();
        var hosted = CreateHosted(
            cluster.Runtime,
            cluster.Joining,
            cluster.State,
            cluster.Table,
            new RecordingProbeTransport((_, _, _) => false),
            services);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            hosted.ValidateStartupConnectivityAsync(TestContext.Current.CancellationToken).AsTask());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task StartupConnectivityEvictsStaleUnreachableMember(int reverseStatus)
    {
        var cluster = await CreateJoiningNodeAgainstActivePeerAsync(TimeSpan.FromMinutes(11));
        await using var services = new ServiceCollection().BuildServiceProvider();
        var hosted = CreateHosted(
            cluster.Runtime,
            cluster.Joining,
            cluster.State,
            cluster.Table,
            new RecordingProbeTransport((_, _, forward) => forward
                ? (MembershipProbeStatus)reverseStatus : MembershipProbeStatus.Failed),
            services);

        await hosted.ValidateStartupConnectivityAsync(TestContext.Current.CancellationToken);

        var snapshot = await cluster.Table.ReadOrCreateAsync(TestContext.Current.CancellationToken);
        Assert.Equal(MembershipTableStatus.Dead, snapshot.Entries.Single(entry => entry.Reference == cluster.Active).Status);
    }

    [Fact]
    public async Task ExactlyOneActiveNodeOwnsDefunctCleanupForAStableView()
    {
        var cluster = await CreateActiveClusterAsync(5);
        var snapshot = cluster.States[0].Current;

        var owners = snapshot.Members.Count(member =>
            MembershipTableHostedService.ShouldRunDefunctCleanup(snapshot, member.Reference));

        Assert.Equal(1, owners);
    }

    [Fact]
    public async Task BeginStoppingPublishesStoppingBeforeStopPublishesDead()
    {
        var table = new InMemoryMembershipTable();
        var membership = new ClusterMembershipState();
        var runtime = CreateRuntime();
        var manager = new MembershipTableManager(
            new NodeId(runtime.Node.Id),
            NodeIncarnationId.New(),
            new NodeEndpoint(runtime.Cluster.Endpoint),
            new ClusterBuildTag("TestBuild1"),
            table,
            membership);
        var gate = new DistributedWorkAdmissionGate();
        await using var services = new ServiceCollection().BuildServiceProvider();
        var hosted = new MembershipTableHostedService(
            runtime,
            manager,
            membership,
            new SingleNodeProbeTransport(),
            gate,
            [],
            services,
            NullLogger<MembershipTableHostedService>.Instance);

        await hosted.StartAsync(TestContext.Current.CancellationToken);
        await hosted.BeginStoppingAsync(TestContext.Current.CancellationToken);

        var stopping = await table.ReadOrCreateAsync(TestContext.Current.CancellationToken);
        Assert.Equal(MembershipTableStatus.Stopping, Assert.Single(stopping.Entries).Status);

        await hosted.StopAsync(TestContext.Current.CancellationToken);

        var dead = await table.ReadOrCreateAsync(TestContext.Current.CancellationToken);
        Assert.Equal(MembershipTableStatus.Dead, Assert.Single(dead.Entries).Status);
    }

    [Fact]
    public async Task ProlongedMembershipTableLossClosesAdmissionAndStopsTheProcess()
    {
        var table = new FailingMembershipTable(new InMemoryMembershipTable());
        var membership = new ClusterMembershipState();
        var runtime = CreateRuntime();
        var manager = new MembershipTableManager(
            new NodeId(runtime.Node.Id),
            NodeIncarnationId.New(),
            new NodeEndpoint(runtime.Cluster.Endpoint),
            new ClusterBuildTag("TestBuild1"),
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
        Assert.False(gate.IsOpen);
        gate.Open();
        table.Fail = true;

        await lifetime.Stopped.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        Assert.False(gate.IsOpen);
        await hosted.StopAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Pending_table_read_cannot_keep_admission_open_after_expiry_or_disposal(bool honorsCancellation, bool dispose)
    {
        var time = new MembershipTestTimeProvider();
        var table = new FailingMembershipTable(new InMemoryMembershipTable());
        var membership = new ClusterMembershipState();
        var runtime = CreateRuntime();
        var manager = new MembershipTableManager(new NodeId(runtime.Node.Id), NodeIncarnationId.New(),
            new NodeEndpoint(runtime.Cluster.Endpoint), new ClusterBuildTag("TestBuild1"), table, membership);
        var gate = new DistributedWorkAdmissionGate(time);
        var lifetime = new TestApplicationLifetime();
        await using var services = new ServiceCollection().BuildServiceProvider();
        using var hosted = new MembershipTableHostedService(runtime, manager, membership, new SingleNodeProbeTransport(),
            gate, [], services, NullLogger<MembershipTableHostedService>.Instance, lifetime, time);
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await hosted.StartAsync(TestContext.Current.CancellationToken);
        gate.Open();
        table.BeforeRead = async ct =>
        {
            entered.TrySetResult(ct);
            await resume.Task.WaitAsync(honorsCancellation ? ct : CancellationToken.None);
        };
        await time.DelayScheduled.Task.WaitAsync(TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromSeconds(1));
        var operationToken = await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        if (dispose) hosted.Dispose();
        else time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(!dispose, lifetime.Stopped.Task.IsCompleted);
        Assert.True(operationToken.IsCancellationRequested);
        Assert.False(gate.TryEnter(out _));
        // Even a successful late result cannot revive this incarnation.
        resume.TrySetResult();
        await hosted.ExecuteTask!.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Throws<InvalidOperationException>(gate.Open);
        table.BeforeRead = null;
        if (!dispose) await hosted.StopAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, time.ActiveTimers);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Slow_peer_operations_cannot_extend_lost_table_contact_budget(bool startupGossip)
    {
        var peer = await CreateActiveClusterAsync(1);
        var time = new MembershipTestTimeProvider();
        var table = new FailingMembershipTable(peer.Table);
        var membership = new ClusterMembershipState();
        var manager = new MembershipTableManager(new NodeId("joining"), NodeIncarnationId.New(),
            new NodeEndpoint("tcp://127.0.0.1:21002"), new ClusterBuildTag("TestBuild1"), table, membership);
        var gate = new DistributedWorkAdmissionGate(time);
        var lifetime = new TestApplicationLifetime();
        var probes = new SlowPeerTransport(startupGossip);
        await using var services = new ServiceCollection().BuildServiceProvider();
        using var hosted = new MembershipTableHostedService(CreateRuntime(), manager, membership, probes,
            gate, [], services, NullLogger<MembershipTableHostedService>.Instance, lifetime, time);
        await hosted.StartAsync(TestContext.Current.CancellationToken);
        var operationToken = await probes.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        gate.Open();
        table.Fail = true;
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.True(lifetime.Stopped.Task.IsCompleted);
        Assert.True(operationToken.IsCancellationRequested);
        Assert.False(gate.IsOpen);
        probes.Resume.TrySetResult();
        await hosted.ExecuteTask!.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Throws<InvalidOperationException>(gate.Open);
        await hosted.StopAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Slow_peers_do_not_block_table_maintenance_or_accumulate_rounds(bool startupGossip)
    {
        var peers = await CreateActiveClusterAsync(3);
        var time = new MembershipTestTimeProvider();
        var membership = new ClusterMembershipState();
        var manager = new MembershipTableManager(new NodeId("joining"), NodeIncarnationId.New(),
            new NodeEndpoint("tcp://127.0.0.1:21004"), new ClusterBuildTag("TestBuild1"), peers.Table, membership, time);
        var gate = new DistributedWorkAdmissionGate(time);
        var lifetime = new TestApplicationLifetime();
        var probes = new SlowPeerTransport(startupGossip);
        await using var services = new ServiceCollection().BuildServiceProvider();
        // Four members and the production defaults: 5s refresh, 30s safety window.
        using var hosted = new MembershipTableHostedService(new LakonaGameRuntimeOptions(), manager, membership,
            probes, gate, [], services, NullLogger<MembershipTableHostedService>.Instance, lifetime, time);
        try
        {
            await hosted.StartAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            var peerToken = await probes.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            gate.Open();
            await time.WaitForDelayAsync(TimeSpan.FromSeconds(5));
            for (var tick = 0; tick < 21; tick++)
            {
                time.Advance(TimeSpan.FromSeconds(5));
                await time.WaitForDelayAsync(TimeSpan.FromSeconds(5));
                Assert.True(gate.IsOpen);
                Assert.False(lifetime.Stopped.Task.IsCompleted);
            }
            Assert.Equal(1, probes.BlockedCalls);
            Assert.False(peerToken.IsCancellationRequested);
            var snapshot = await peers.Table.ReadOrCreateAsync(TestContext.Current.CancellationToken);
            Assert.True(snapshot.Entries.Single(e => e.Reference == manager.Local).IAmAliveTime >=
                DateTimeOffset.UnixEpoch.AddSeconds(90));

            await hosted.BeginStoppingAsync(TestContext.Current.CancellationToken);
            Assert.True(peerToken.IsCancellationRequested);
            // Table maintenance remains alive during Actor cleanup, after Stopping.
            time.Advance(TimeSpan.FromSeconds(5));
            await time.WaitForDelayAsync(TimeSpan.FromSeconds(5));
            Assert.False(hosted.ExecuteTask!.IsCompleted);
            Assert.False(lifetime.Stopped.Task.IsCompleted);
            probes.Result = MembershipProbeStatus.Failed;
        }
        finally
        {
            probes.Resume.TrySetResult();
            await hosted.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
        Assert.True(hosted.ExecuteTask!.IsCompletedSuccessfully);
        Assert.Equal(1, probes.BlockedCalls);
        Assert.Equal(0, time.ActiveTimers);
        var final = await peers.Table.ReadOrCreateAsync(TestContext.Current.CancellationToken);
        Assert.All(final.Entries.Where(e => e.Reference != manager.Local), e => Assert.Empty(e.SuspectVotes));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task Stop_joins_a_late_peer_before_publishing_dead(bool startupGossip, bool expireStop)
    {
        var peers = await CreateActiveClusterAsync(2, failedProbesBeforeSuspect: 1);
        var time = new MembershipTestTimeProvider();
        var membership = new ClusterMembershipState();
        var manager = new MembershipTableManager(new NodeId("joining"), NodeIncarnationId.New(),
            new NodeEndpoint("tcp://127.0.0.1:21004"), new ClusterBuildTag("TestBuild1"), peers.Table, membership, time);
        var probes = new SlowPeerTransport(startupGossip) { Result = MembershipProbeStatus.Failed };
        await using var services = new ServiceCollection().BuildServiceProvider();
        using var hosted = new MembershipTableHostedService(peers.Runtime, manager, membership,
            probes, new DistributedWorkAdmissionGate(time), [], services,
            NullLogger<MembershipTableHostedService>.Instance, timeProvider: time);
        try
        {
            await hosted.StartAsync(TestContext.Current.CancellationToken);
            var peerToken = await probes.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            using var stopBudget = new CancellationTokenSource();
            var stopping = hosted.StopAsync(stopBudget.Token);
            Assert.True(peerToken.IsCancellationRequested);
            Assert.False(stopping.IsCompleted);
            var snapshot = await peers.Table.ReadOrCreateAsync(TestContext.Current.CancellationToken);
            Assert.Equal(MembershipTableStatus.Stopping, snapshot.Entries.Single(e => e.Reference == manager.Local).Status);
            if (expireStop)
            {
                stopBudget.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stopping);
                Assert.False(hosted.ExecuteTask!.IsCompleted);
                snapshot = await peers.Table.ReadOrCreateAsync(TestContext.Current.CancellationToken);
                Assert.Equal(MembershipTableStatus.Stopping, snapshot.Entries.Single(e => e.Reference == manager.Local).Status);
            }
            probes.Resume.TrySetResult();
            if (expireStop) await hosted.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            else await stopping.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.True(hosted.ExecuteTask!.IsCompletedSuccessfully);
            Assert.Equal(1, probes.BlockedCalls);
            snapshot = await peers.Table.ReadOrCreateAsync(TestContext.Current.CancellationToken);
            Assert.Equal(MembershipTableStatus.Dead, snapshot.Entries.Single(e => e.Reference == manager.Local).Status);
            Assert.All(snapshot.Entries.Where(e => e.Reference != manager.Local), e => Assert.Empty(e.SuspectVotes));
            Assert.Equal(0, time.ActiveTimers);
        }
        finally
        {
            probes.Resume.TrySetResult();
            await hosted.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Gossip_request_timeout_does_not_cancel_the_membership_lifetime(bool canceledRequest)
    {
        var peers = await CreateActiveClusterAsync(2);
        var time = new MembershipTestTimeProvider();
        var membership = new ClusterMembershipState();
        var manager = new MembershipTableManager(new NodeId("joining"), NodeIncarnationId.New(),
            new NodeEndpoint("tcp://127.0.0.1:21004"), new ClusterBuildTag("TestBuild1"), peers.Table, membership, time);
        var probes = new SlowPeerTransport(false)
        {
            GossipError = canceledRequest
                ? new OperationCanceledException(new CancellationToken(canceled: true))
                : new TimeoutException("Peer RPC timed out")
        };
        var gate = new DistributedWorkAdmissionGate(time);
        var lifetime = new TestApplicationLifetime();
        await using var services = new ServiceCollection().BuildServiceProvider();
        using var hosted = new MembershipTableHostedService(new LakonaGameRuntimeOptions(), manager, membership,
            probes, gate, [], services, NullLogger<MembershipTableHostedService>.Instance, lifetime, time);
        try
        {
            await hosted.StartAsync(TestContext.Current.CancellationToken);
            var token = await probes.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Equal(2, probes.GossipCalls);
            Assert.False(token.IsCancellationRequested);
            gate.Open();
            await time.WaitForDelayAsync(TimeSpan.FromSeconds(5));
            time.Advance(TimeSpan.FromSeconds(5));
            await time.WaitForDelayAsync(TimeSpan.FromSeconds(5));
            Assert.True(gate.IsOpen);
            Assert.False(lifetime.Stopped.Task.IsCompleted);
        }
        finally
        {
            // Cancel first, so releasing the delayed probe cannot begin another round.
            await hosted.BeginStoppingAsync(CancellationToken.None);
            probes.Resume.TrySetResult();
            await hosted.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
        Assert.Equal(0, time.ActiveTimers);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task Unexpected_worker_exit_fences_and_cancels_the_other_worker(bool failTableWorker, bool cancellationException)
    {
        var peers = await CreateActiveClusterAsync(1);
        var time = new MembershipTestTimeProvider();
        Exception failure = cancellationException
            ? new OperationCanceledException("Injected scheduling failure")
            : new InvalidOperationException("Injected scheduling failure");
        time.DelayFailure = delay => delay == TimeSpan.FromSeconds(failTableWorker ? 5 : 10) ? failure : null;
        var membership = new ClusterMembershipState();
        var manager = new MembershipTableManager(new NodeId("joining"), NodeIncarnationId.New(),
            new NodeEndpoint("tcp://127.0.0.1:21004"), new ClusterBuildTag("TestBuild1"), peers.Table, membership, time);
        var probes = new SlowPeerTransport(failTableWorker);
        var gate = new DistributedWorkAdmissionGate(time);
        var lifetime = new TestApplicationLifetime();
        await using var services = new ServiceCollection().BuildServiceProvider();
        using var hosted = new MembershipTableHostedService(new LakonaGameRuntimeOptions(), manager, membership,
            probes, gate, [], services, NullLogger<MembershipTableHostedService>.Instance, lifetime, time);
        try
        {
            await hosted.StartAsync(TestContext.Current.CancellationToken);
            var token = await probes.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            if (!failTableWorker) probes.Resume.TrySetResult();
            await lifetime.Stopped.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            // StopApplication is signaled before cancellation/join; wait for the actual token too.
            var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = token.Register(() => canceled.TrySetResult());
            await canceled.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.False(gate.IsOpen);
            Assert.Throws<InvalidOperationException>(gate.Open);
            if (failTableWorker) Assert.False(hosted.ExecuteTask!.IsCompleted);
            probes.Resume.TrySetResult();
            var actual = await Record.ExceptionAsync(() => hosted.ExecuteTask!);
            Assert.Same(failure, actual);
        }
        finally
        {
            probes.Resume.TrySetResult();
            Assert.Same(failure, await Record.ExceptionAsync(() => hosted.StopAsync(CancellationToken.None)));
        }
        Assert.Equal(0, time.ActiveTimers);
    }

    [Fact]
    public async Task MembershipSchemaFailureStopsStartupWithoutRetrying()
    {
        var table = new SchemaMismatchMembershipTable();
        var membership = new ClusterMembershipState();
        var runtime = CreateRuntime();
        var manager = new MembershipTableManager(
            new NodeId(runtime.Node.Id),
            NodeIncarnationId.New(),
            new NodeEndpoint(runtime.Cluster.Endpoint),
            new ClusterBuildTag("TestBuild1"),
            table,
            membership);
        await using var services = new ServiceCollection().BuildServiceProvider();
        var hosted = new MembershipTableHostedService(
            runtime,
            manager,
            membership,
            new SingleNodeProbeTransport(),
            new DistributedWorkAdmissionGate(),
            [],
            services,
            NullLogger<MembershipTableHostedService>.Instance);

        await Assert.ThrowsAsync<MembershipSchemaException>(() =>
            hosted.StartAsync(TestContext.Current.CancellationToken));

        Assert.Equal(1, table.AllocateAttempts);
    }

    private static LakonaGameRuntimeOptions CreateRuntime() => new()
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

    private static MembershipTableHostedService CreateHosted(
        LakonaGameRuntimeOptions runtime,
        MembershipTableManager manager,
        ClusterMembershipState membership,
        IMembershipTable table,
        IMembershipProbeTransport probes,
        IServiceProvider services) =>
        new(
            runtime,
            manager,
            membership,
            probes,
            new DistributedWorkAdmissionGate(),
            [],
            services,
            NullLogger<MembershipTableHostedService>.Instance);

    private static async Task<ActiveCluster> CreateActiveClusterAsync(
        int count,
        int failedProbesBeforeSuspect = 3, int votesForDeath = 1, NodeEndpoint? peerEndpoint = null)
    {
        var table = new InMemoryMembershipTable();
        var managers = new List<MembershipTableManager>();
        var states = new List<ClusterMembershipState>();
        var runtime = new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "server-1" },
            Cluster = new LakonaGameClusterOptions
            {
                Endpoint = "tcp://127.0.0.1:21001",
                Membership = new LakonaGameMembershipOptions
                {
                    FailedProbesBeforeSuspect = failedProbesBeforeSuspect,
                    VotesForDeath = votesForDeath
                }
            }
        };
        for (var index = 0; index < count; index++)
        {
            var state = new ClusterMembershipState();
            var manager = new MembershipTableManager(
                new NodeId($"server-{index + 1}"),
                new NodeIncarnationId(Guid.Parse($"{index + 1:x8}-1111-1111-1111-111111111111")),
                index == 1 && peerEndpoint is not null ? peerEndpoint : new NodeEndpoint($"tcp://127.0.0.1:{21001 + index}"),
                new ClusterBuildTag("TestBuild1"),
                table,
                state);
            await manager.JoinAsync(TestContext.Current.CancellationToken);
            await manager.ActivateAsync(null, [], [], TestContext.Current.CancellationToken);
            managers.Add(manager);
            states.Add(state);
        }

        foreach (var manager in managers)
            await manager.RefreshAsync(TestContext.Current.CancellationToken);
        return new ActiveCluster(runtime, table, managers, states);
    }

    private static async Task<StartupCluster> CreateJoiningNodeAgainstActivePeerAsync(TimeSpan peerAge)
    {
        var table = new InMemoryMembershipTable();
        var oldTime = new MutableTimeProvider();
        var activeState = new ClusterMembershipState();
        var activeManager = new MembershipTableManager(
            new NodeId("server-1"),
            NodeIncarnationId.New(),
            new NodeEndpoint("tcp://127.0.0.1:21001"),
            new ClusterBuildTag("TestBuild1"),
            table,
            activeState,
            oldTime);
        var active = await activeManager.JoinAsync(TestContext.Current.CancellationToken);
        await activeManager.ActivateAsync(null, [], [], TestContext.Current.CancellationToken);
        var currentTime = new MutableTimeProvider();
        currentTime.Advance(peerAge);
        var state = new ClusterMembershipState();
        var joining = new MembershipTableManager(
            new NodeId("server-2"),
            NodeIncarnationId.New(),
            new NodeEndpoint("tcp://127.0.0.1:21002"),
            new ClusterBuildTag("TestBuild1"),
            table,
            state,
            currentTime);
        await joining.JoinAsync(TestContext.Current.CancellationToken);
        var runtime = new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "server-2" },
            Cluster = new LakonaGameClusterOptions
            {
                Endpoint = "tcp://127.0.0.1:21002",
                Membership = new LakonaGameMembershipOptions { AllowedIAmAliveMissSeconds = 600 }
            }
        };
        return new StartupCluster(runtime, table, joining, state, active);
    }

    private sealed record ActiveCluster(
        LakonaGameRuntimeOptions Runtime,
        InMemoryMembershipTable Table,
        IReadOnlyList<MembershipTableManager> Managers,
        IReadOnlyList<ClusterMembershipState> States);

    private sealed record StartupCluster(
        LakonaGameRuntimeOptions Runtime,
        InMemoryMembershipTable Table,
        MembershipTableManager Joining,
        ClusterMembershipState State,
        NodeReference Active);

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.Parse("2026-08-25T00:00:00Z");
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }

    private sealed class RecordingProbeTransport(
        Func<ClusterMember, NodeEndpoint, bool, MembershipProbeStatus> result) : IMembershipProbeTransport
    {
        public RecordingProbeTransport(Func<ClusterMember, NodeEndpoint, bool, bool> result)
            : this((member, contact, forward) => result(member, contact, forward)
                ? MembershipProbeStatus.Succeeded : MembershipProbeStatus.Failed) { }

        public List<(NodeReference Source, NodeReference Target, NodeEndpoint Contact, bool Forward)> Calls { get; } = [];

        public ValueTask<MembershipProbeStatus> ProbeAsync(
            NodeReference source,
            ClusterMember target,
            NodeEndpoint contact,
            bool forward,
            TimeSpan? probeTimeout = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((source, target.Reference, contact, forward));
            return new(result(target, contact, forward));
        }

        public ValueTask GossipAsync(
            NodeReference source,
            NodeEndpoint contact,
            MembershipViewId version,
            CancellationToken cancellationToken = default) => default;
    }

    private sealed class FailingMembershipTable(IMembershipTable inner) : IMembershipTable
    {
        public bool Fail { get; set; }
        public Func<CancellationToken, ValueTask>? BeforeRead { get; set; }
        public Exception? AllocationError { get; set; }
        public Action? AfterAllocate { get; set; }
        public int AllocateAttempts { get; private set; }

        public async ValueTask<MembershipTableGeneration> AllocateGenerationAsync(
            string buildTag,
            CancellationToken cancellationToken = default)
        {
            AllocateAttempts++;
            if (AllocationError is not null) throw AllocationError;
            var result = await Invoke(() => inner.AllocateGenerationAsync(buildTag, cancellationToken));
            AfterAllocate?.Invoke();
            return result;
        }

        public async ValueTask<MembershipTableSnapshot> ReadOrCreateAsync(CancellationToken cancellationToken = default)
        {
            if (BeforeRead is not null) await BeforeRead(cancellationToken);
            // The test can model a provider which returns success after cancellation.
            return await Invoke(() => inner.ReadOrCreateAsync());
        }

        public ValueTask<bool> TryInsertAsync(MembershipTableEntry entry, MembershipViewId expectedVersion, CancellationToken cancellationToken = default) =>
            Invoke(() => inner.TryInsertAsync(entry, expectedVersion, cancellationToken));

        public ValueTask<bool> TryReplaceAsync(NodeReference previous, long expectedPreviousVersion, MembershipTableEntry replacement, MembershipViewId expectedVersion, CancellationToken cancellationToken = default) =>
            Invoke(() => inner.TryReplaceAsync(previous, expectedPreviousVersion, replacement, expectedVersion, cancellationToken));

        public ValueTask<bool> TryUpdateAsync(MembershipTableEntry entry, long expectedEntryVersion, MembershipViewId expectedVersion, CancellationToken cancellationToken = default) =>
            Invoke(() => inner.TryUpdateAsync(entry, expectedEntryVersion, expectedVersion, cancellationToken));

        public ValueTask<bool> TryUpdateIAmAliveAsync(NodeReference reference, DateTimeOffset timestamp, CancellationToken cancellationToken = default) =>
            Invoke(() => inner.TryUpdateIAmAliveAsync(reference, timestamp, cancellationToken));

        public ValueTask<int> CleanupDefunctAsync(DateTimeOffset before, int maximumRows, CancellationToken cancellationToken = default) =>
            Invoke(() => inner.CleanupDefunctAsync(before, maximumRows, cancellationToken));

        private ValueTask<T> Invoke<T>(Func<ValueTask<T>> action)
        {
            if (Fail) throw new InvalidOperationException("Membership Table unavailable.");
            return action();
        }
    }

    private sealed class SchemaMismatchMembershipTable : IMembershipTable
    {
        public int AllocateAttempts { get; private set; }

        public ValueTask<MembershipTableGeneration> AllocateGenerationAsync(
            string buildTag,
            CancellationToken cancellationToken = default)
        {
            AllocateAttempts++;
            throw new MembershipSchemaException(
                "Apply database/postgresql/membership.sql.",
                new InvalidOperationException("Missing table."));
        }

        public ValueTask<MembershipTableSnapshot> ReadOrCreateAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<bool> TryInsertAsync(MembershipTableEntry entry, MembershipViewId expectedVersion, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<bool> TryReplaceAsync(NodeReference previous, long expectedPreviousVersion, MembershipTableEntry replacement, MembershipViewId expectedVersion, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<bool> TryUpdateAsync(MembershipTableEntry entry, long expectedEntryVersion, MembershipViewId expectedVersion, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<bool> TryUpdateIAmAliveAsync(NodeReference reference, DateTimeOffset timestamp, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<int> CleanupDefunctAsync(DateTimeOffset before, int maximumRows, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class SlowPeerTransport(bool startupGossip) : IMembershipProbeTransport
    {
        private bool startupValidated;
        private int blockedCalls;
        private int gossipCalls;
        public int BlockedCalls => Volatile.Read(ref blockedCalls);
        public int GossipCalls => Volatile.Read(ref gossipCalls);
        public Exception? GossipError { get; init; }
        public MembershipProbeStatus Result { get; set; } = MembershipProbeStatus.Succeeded;
        public TaskCompletionSource<CancellationToken> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<MembershipProbeStatus> ProbeAsync(NodeReference source, ClusterMember target,
            NodeEndpoint contact, bool forward, TimeSpan? probeTimeout = null, CancellationToken cancellationToken = default)
        {
            if (!startupValidated) return MembershipProbeStatus.Succeeded;
            await BlockAsync(cancellationToken);
            return Result;
        }

        public async ValueTask GossipAsync(NodeReference source, NodeEndpoint contact, MembershipViewId version,
            CancellationToken cancellationToken = default)
        {
            startupValidated = true;
            Interlocked.Increment(ref gossipCalls);
            if (GossipError is { } error) throw error;
            if (startupGossip) await BlockAsync(cancellationToken);
        }

        private async Task BlockAsync(CancellationToken token)
        {
            Interlocked.Increment(ref blockedCalls);
            Entered.TrySetResult(token);
            await Resume.Task; // Deliberately returns success despite cancellation.
        }
    }

    private sealed class SingleNodeProbeTransport : IMembershipProbeTransport
    {
        public ValueTask<MembershipProbeStatus> ProbeAsync(NodeReference source, ClusterMember target, NodeEndpoint contact, bool forward, TimeSpan? probeTimeout = null, CancellationToken cancellationToken = default) =>
            new(MembershipProbeStatus.Failed);

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
