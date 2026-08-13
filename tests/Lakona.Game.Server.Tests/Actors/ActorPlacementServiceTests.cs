using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Dispatch;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using GameActor = Lakona.Game.Server.Actors.Actor;

namespace Lakona.Game.Server.Tests.Actors;

public sealed class ActorPlacementServiceTests
{
    [Fact]
    public async Task PlaceAsyncUsesExistingRouteBeforeSelectingCandidate()
    {
        var selector = new RecordingSelector();
        var actorId = ActorId.From("room-1");
        var service = CreateService(
            existingOwner: new NodeId("battle-1"),
            placements: [ActorPlacementDeclaration.Create<RoomActor, ActorId>(selector.Select)]);

        var result = await service.PlaceAsync<RoomActor, ActorId>(
            actorId,
            ActorPlacementCreateMode.Ensure,
            TestContext.Current.CancellationToken);

        Assert.Equal(new NodeId("battle-1"), result.Owner);
        Assert.False(selector.WasCalled);
    }

    [Fact]
    public async Task CreateAsyncRejectsExistingActivationBeforeSelectingCandidate()
    {
        var selector = new RecordingSelector();
        var actorId = ActorId.From("room-existing");
        var service = CreateService(
            existingOwner: new NodeId("battle-existing"),
            placements: [ActorPlacementDeclaration.Create<RoomActor, ActorId>(selector.Select)]);

        var exception = await Assert.ThrowsAsync<ActorPlacementException>(async () =>
            await service.PlaceAsync<RoomActor, ActorId>(
                actorId,
                ActorPlacementCreateMode.Create,
                TestContext.Current.CancellationToken));

        Assert.Equal(actorId, exception.ActorId);
        Assert.Contains("already has an activation", exception.Message, StringComparison.Ordinal);
        Assert.Contains("battle-existing", exception.Message, StringComparison.Ordinal);
        Assert.False(selector.WasCalled);
    }

    [Fact]
    public async Task PlaceAsyncSelectsCandidateAndRequestsCreate()
    {
        var actorId = ActorId.From("room-2");
        var hostClient = new RecordingActorHostClient();
        var service = CreateService(
            candidates: [new NodeId("battle-2")],
            hostClient: hostClient,
            placements:
            [
                ActorPlacementDeclaration.Create<RoomActor, ActorId>(
                    static context => context.Candidates[0])
            ]);

        var result = await service.PlaceAsync<RoomActor, ActorId>(
            actorId,
            ActorPlacementCreateMode.Create,
            TestContext.Current.CancellationToken);

        Assert.Equal(new NodeId("battle-2"), result.Owner);
        Assert.Equal(new NodeId("battle-2"), hostClient.LastNode);
        Assert.NotNull(hostClient.LastRequest);
        Assert.Equal("room", hostClient.LastRequest.Actor);
        Assert.Equal("room-2", hostClient.LastRequest.ActorId);
        Assert.Equal("create", hostClient.LastRequest.Mode);
    }

    [Fact]
    public async Task PlaceAsync_fails_closed_when_selected_capability_is_withdrawn_after_selection()
    {
        var membership = new MutableMembership(Snapshot(Member("battle-1", 1, hostsActor: true)));
        var directory = new RecordingActivationDirectory();
        var hostClient = new RecordingActorHostClient();
        var service = CreateClusterService(
            membership,
            directory,
            hostClient,
            ActorPlacementDeclaration.Create<RoomActor, ActorId>(context =>
            {
                membership.Current = Snapshot(Member("battle-1", 1, hostsActor: false));
                return context.Candidates[0];
            }));

        await Assert.ThrowsAsync<ActorPlacementException>(async () => await service.PlaceAsync<RoomActor, ActorId>(
            ActorId.From("room-stale"),
            ActorPlacementCreateMode.Create,
            TestContext.Current.CancellationToken));

        Assert.Equal(0, directory.AcquireCalls);
        Assert.Null(hostClient.LastNode);
    }

    [Fact]
    public async Task PlaceAsync_acquires_activation_with_current_exact_reference_after_selection()
    {
        var membership = new MutableMembership(Snapshot(Member("battle-1", 1, hostsActor: true)));
        var directory = new RecordingActivationDirectory();
        var hostClient = new RecordingActorHostClient();
        var service = CreateClusterService(
            membership,
            directory,
            hostClient,
            ActorPlacementDeclaration.Create<RoomActor, ActorId>(context =>
            {
                membership.Current = Snapshot(Member("battle-1", 2, hostsActor: true));
                return context.Candidates[0];
            }));

        await service.PlaceAsync<RoomActor, ActorId>(
            ActorId.From("room-reincarnated"),
            ActorPlacementCreateMode.Create,
            TestContext.Current.CancellationToken);

        var expected = membership.Current.Members.Single().Reference;
        Assert.Equal(1, directory.AcquireCalls);
        Assert.Equal(expected, directory.ProposedOwner);
        Assert.Equal(new NodeId("battle-1"), hostClient.LastNode);
    }

    [Fact]
    public async Task PlaceAsyncWithoutRegistrationUsesRendezvousSelection()
    {
        var hostClient = new RecordingActorHostClient();
        var service = CreateService(
            candidates: [new NodeId("node-2"), new NodeId("node-3"), new NodeId("node-1")],
            hostClient: hostClient);

        var result = await service.PlaceAsync<RoomActor, string>(
            "tenant-a",
            ActorPlacementCreateMode.Create,
            TestContext.Current.CancellationToken);

        Assert.Equal(new NodeId("node-3"), result.Owner);
        Assert.Equal(new NodeId("node-3"), hostClient.LastNode);
    }

    [Fact]
    public async Task PlaceAsyncRejectsMissingCandidates()
    {
        var service = CreateService(
            placements:
            [
                ActorPlacementDeclaration.Create<RoomActor, ActorId>(
                    static context => context.Candidates[0])
            ]);

        var exception = await Assert.ThrowsAsync<ActorPlacementException>(async () =>
            await service.PlaceAsync<RoomActor, ActorId>(
                ActorId.From("room-3"),
                ActorPlacementCreateMode.Ensure,
                TestContext.Current.CancellationToken));

        Assert.Contains("No actor host candidates", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlaceAsyncRejectsSelectorReturningUnknownCandidate()
    {
        var service = CreateService(
            candidates: [new NodeId("battle-4")],
            placements:
            [
                ActorPlacementDeclaration.Create<RoomActor, ActorId>(
                    static _ => new ActorHostCandidate("missing"))
            ]);

        var exception = await Assert.ThrowsAsync<ActorPlacementException>(async () =>
            await service.PlaceAsync<RoomActor, ActorId>(
                ActorId.From("room-4"),
                ActorPlacementCreateMode.Ensure,
                TestContext.Current.CancellationToken));

        Assert.Contains("not one of the discovered candidates", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlaceAsyncReturnsConflictOwnerFromHostReply()
    {
        var actorId = ActorId.From("room-5");
        var hostClient = new RecordingActorHostClient
        {
            Reply = new ActorHostCreateReply(false, "battle-existing", "already hosted")
        };
        var service = CreateService(
            candidates: [new NodeId("battle-5")],
            hostClient: hostClient,
            placements:
            [
                ActorPlacementDeclaration.Create<RoomActor, ActorId>(
                    static context => context.Candidates[0])
            ]);

        var result = await service.PlaceAsync<RoomActor, ActorId>(
            actorId,
            ActorPlacementCreateMode.Ensure,
            TestContext.Current.CancellationToken);

        Assert.Equal(new NodeId("battle-existing"), result.Owner);
    }

    [Fact]
    public async Task CreateAsyncRejectsConflictOwnerFromHostReply()
    {
        var actorId = ActorId.From("room-conflict");
        var hostClient = new RecordingActorHostClient
        {
            Reply = new ActorHostCreateReply(false, "battle-existing", "already hosted")
        };
        var service = CreateService(
            candidates: [new NodeId("battle-5")],
            hostClient: hostClient,
            placements:
            [
                ActorPlacementDeclaration.Create<RoomActor, ActorId>(
                    static context => context.Candidates[0])
            ]);

        var exception = await Assert.ThrowsAsync<ActorPlacementException>(async () =>
            await service.PlaceAsync<RoomActor, ActorId>(
                actorId,
                ActorPlacementCreateMode.Create,
                TestContext.Current.CancellationToken));

        Assert.Equal(actorId, exception.ActorId);
        Assert.Contains("already has an activation", exception.Message, StringComparison.Ordinal);
        Assert.Contains("battle-existing", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DestroyAsync_sends_the_exact_activation_to_its_current_owner()
    {
        var owner = Member("battle-1", 1, hostsActor: true).Reference;
        var actorId = ActorId.From("room-destroy");
        var activation = new ActorActivationId(Guid.Parse("70000000-0000-0000-0000-000000000001"));
        var directory = new RecordingActivationDirectory
        {
            Current = new ActorDirectoryRecord(actorId, owner, activation, 17, DateTimeOffset.UtcNow)
        };
        var hostClient = new RecordingActorHostClient();
        var membership = new MutableMembership(Snapshot(Member("battle-1", 1, hostsActor: true)));
        var service = CreateClusterService(
            membership,
            directory,
            hostClient,
            ActorPlacementDeclaration.Create<RoomActor, ActorId>(static context => context.Candidates[0]));

        await service.DestroyAsync<RoomActor>(
            actorId,
            TestContext.Current.CancellationToken);

        Assert.Equal(owner.Node, hostClient.LastDestroyNode);
        Assert.NotNull(hostClient.LastDestroyRequest);
        Assert.Equal("destroy", hostClient.LastDestroyRequest.Mode);
        Assert.Equal(activation.Value.ToString("D"), hostClient.LastDestroyRequest.ActivationId);
        Assert.Equal(17, hostClient.LastDestroyRequest.ActivationVersion);
    }

    [Fact]
    public async Task DestroyAsync_is_idempotent_when_no_activation_exists()
    {
        var service = CreateService();

        await service.DestroyAsync<RoomActor>(
            ActorId.From("room-missing"),
            TestContext.Current.CancellationToken);
    }

    private static ActorPlacementService CreateService(
        NodeId? existingOwner = null,
        IReadOnlyList<NodeId>? candidates = null,
        IReadOnlyList<ActorPlacementDeclaration>? placements = null,
        RecordingActorHostClient? hostClient = null)
    {
        var actorDirectory = new FakeActorDirectory(existingOwner);
        var membership = new FixedMembership(candidates ?? []);
        hostClient ??= new RecordingActorHostClient();
        var runtime = new FixedHotfixRuntimeAccessor(new HotfixRuntimeSnapshot(
            new HotfixServiceInvoker(new HotfixDispatchTable(1, [], [])),
            new ServiceCollection().BuildServiceProvider(),
            dispatchTable: null,
            hotfixServices: null,
            mainAssembly: null,
            loadContext: null,
            sourceVersion: null,
            sourcePath: null,
            ownsRuntimeResources: false,
            onRetired: null,
            actorPlacements: placements ?? []));

        return new ActorPlacementService(
            actorDirectory,
            new ClusterCapabilityIndex(membership),
            hostClient,
            actorHosting: null!,
            new LocalActorNodeIdentity("local"),
            runtime,
            membership);
    }

    private static ActorPlacementService CreateClusterService(
        MutableMembership membership,
        RecordingActivationDirectory directory,
        RecordingActorHostClient hostClient,
        ActorPlacementDeclaration placement)
    {
        var runtime = new FixedHotfixRuntimeAccessor(new HotfixRuntimeSnapshot(
            new HotfixServiceInvoker(new HotfixDispatchTable(1, [], [])),
            new ServiceCollection().BuildServiceProvider(),
            actorPlacements: [placement]));
        return new ActorPlacementService(
            directory,
            new ClusterCapabilityIndex(membership),
            hostClient,
            actorHosting: null!,
            new LocalActorNodeIdentity("local"),
            runtime,
            membership);
    }

    private static ClusterMembershipSnapshot Snapshot(params ClusterMember[] members) => new(
        new ClusterIncarnationId(Guid.Parse("50000000-0000-0000-0000-000000000000")),
        new MembershipViewId(1),
        members);

    private static ClusterMember Member(string node, int incarnation, bool hostsActor) => new(
        new NodeReference(
            new ClusterIncarnationId(Guid.Parse("50000000-0000-0000-0000-000000000000")),
            new NodeId(node),
            new NodeIncarnationId(Guid.Parse($"{incarnation:D8}-0000-0000-0000-000000000000"))),
        ClusterMemberState.Ready,
        new NodeEndpoint($"tcp://{node}:21000"),
        isVoter: true,
        labels: null,
        actorHosts: hostsActor ? [new NodeActorHostDescriptor("room", "policy", "build")] : [],
        startupActors: null);

    [ActorName("room")]
    private sealed class RoomActor : GameActor;

    private sealed class RecordingSelector
    {
        public bool WasCalled { get; private set; }

        public ActorHostCandidate Select(ActorPlacementContext<ActorId> context)
        {
            WasCalled = true;
            return context.Candidates[0];
        }
    }

    private sealed class RecordingActorHostClient : IActorHostClient
    {
        public NodeId? LastNode { get; private set; }

        public ActorHostCreateRequest? LastRequest { get; private set; }

        public NodeId? LastDestroyNode { get; private set; }

        public ActorHostCreateRequest? LastDestroyRequest { get; private set; }

        public ActorHostCreateReply? Reply { get; init; }

        public ValueTask<ActorHostCreateReply> CreateAsync(
            NodeId node,
            ActorHostCreateRequest request,
            CancellationToken cancellationToken = default)
        {
            LastNode = node;
            LastRequest = request;
            return new ValueTask<ActorHostCreateReply>(Reply ?? new ActorHostCreateReply(true, node.Value, "created"));
        }

        public ValueTask<ActorHostCreateReply> DestroyAsync(
            NodeId node,
            ActorHostCreateRequest request,
            CancellationToken cancellationToken = default)
        {
            LastDestroyNode = node;
            LastDestroyRequest = request;
            return new ValueTask<ActorHostCreateReply>(Reply ?? new ActorHostCreateReply(true, node.Value, "destroyed"));
        }
    }

    private sealed class FakeActorDirectory(NodeId? existingOwner) : IActorDirectory
    {
        public ValueTask<ActorDirectoryRecord?> ResolveAsync(
            ActorId actorId,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<ActorDirectoryRecord?>(existingOwner is null
                ? null
                : new ActorDirectoryRecord(actorId, existingOwner.Value, 1, DateTimeOffset.UtcNow));
        }

        public ValueTask<ActorDirectoryRegisterStatus> RegisterAsync(
            ActorId actorId,
            NodeId node,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<ActorDirectoryRegisterStatus>(ActorDirectoryRegisterStatus.Registered);
        }

        public ValueTask<ActorDirectoryUnregisterStatus> UnregisterAsync(
            ActorId actorId,
            NodeId node,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<ActorDirectoryUnregisterStatus>(ActorDirectoryUnregisterStatus.Unregistered);
        }
    }

    private sealed class FixedMembership(IReadOnlyList<NodeId> candidates) : IClusterMembership
    {
        private static readonly ClusterIncarnationId Cluster = new(Guid.Parse("50000000-0000-0000-0000-000000000000"));
        public ClusterMembershipSnapshot Current { get; } = new(Cluster, new MembershipViewId(1), candidates.Select((node, index) => new ClusterMember(
            new NodeReference(Cluster, node, new NodeIncarnationId(Guid.Parse($"0000000{index + 1}-0000-0000-0000-000000000000"))),
            ClusterMemberState.Ready, new NodeEndpoint("tcp://127.0.0.1:21000"), true,
            labels: null,
            actorHosts: [new NodeActorHostDescriptor("room", "policy", "build")],
            startupActors: null)).ToArray());
        public ValueTask<ClusterMembershipSnapshot> WaitForChangeAsync(MembershipViewId after, CancellationToken cancellationToken = default) => new(Current);
    }

    private sealed class MutableMembership(ClusterMembershipSnapshot current) : IClusterMembership
    {
        public ClusterMembershipSnapshot Current { get; set; } = current;

        public ValueTask<ClusterMembershipSnapshot> WaitForChangeAsync(MembershipViewId after, CancellationToken cancellationToken = default) => new(Current);
    }

    private sealed class RecordingActivationDirectory : IActorDirectory, IActorActivationDirectory
    {
        public ActorDirectoryRecord? Current { get; init; }

        public int AcquireCalls { get; private set; }

        public NodeReference? ProposedOwner { get; private set; }

        public ValueTask<ActorDirectoryRecord?> ResolveAsync(ActorId actorId, CancellationToken cancellationToken = default) => new(Current);

        public ValueTask<ActorDirectoryRegisterStatus> RegisterAsync(ActorId actorId, NodeId node, CancellationToken cancellationToken = default) => new(ActorDirectoryRegisterStatus.Registered);

        public ValueTask<ActorDirectoryUnregisterStatus> UnregisterAsync(ActorId actorId, NodeId node, CancellationToken cancellationToken = default) => new(ActorDirectoryUnregisterStatus.Unregistered);

        public ValueTask<ActorActivationAcquireResult> AcquireAsync(ActorId actorId, NodeReference proposedOwner, ActorActivationId proposedActivation, CancellationToken cancellationToken = default)
        {
            AcquireCalls++;
            ProposedOwner = proposedOwner;
            return new ValueTask<ActorActivationAcquireResult>(new ActorActivationAcquireResult(
                new ActorDirectoryRecord(actorId, proposedOwner, proposedActivation, 1, DateTimeOffset.UtcNow),
                true));
        }

        public ValueTask<bool> ReleaseAsync(ActorId actorId, ActorActivationId expectedActivation, long expectedVersion, CancellationToken cancellationToken = default) => new(true);
    }

    private sealed class FixedHotfixRuntimeAccessor(HotfixRuntimeSnapshot snapshot) : IHotfixRuntimeAccessor
    {
        public HotfixRuntimeSnapshot Current => snapshot;
    }
}
