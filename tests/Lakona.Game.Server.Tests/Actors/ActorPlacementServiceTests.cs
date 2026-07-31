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

    private static ActorPlacementService CreateService(
        NodeId? existingOwner = null,
        IReadOnlyList<NodeId>? candidates = null,
        IReadOnlyList<ActorPlacementDeclaration>? placements = null,
        RecordingActorHostClient? hostClient = null)
    {
        var actorDirectory = new FakeActorDirectory(existingOwner);
        var nodeDirectory = new FakeNodeDiscovery(candidates ?? []);
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
            nodeDirectory,
            hostClient,
            actorHosting: null!,
            new LocalActorNodeIdentity("local"),
            runtime);
    }

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

    private sealed class FakeNodeDiscovery(IReadOnlyList<NodeId> candidates) : IClusterNodeDiscovery
    {
        public ValueTask<IReadOnlyList<ClusterNodeDescriptor>> QueryAsync(
            ClusterNodeDiscoveryQuery query,
            CancellationToken cancellationToken = default)
        {
            var records = candidates
                .Select(node => new ClusterNodeDescriptor(
                    node,
                    NodeState.Ready,
                    new Dictionary<string, NodeEndpoint>
                    {
                        ["cluster"] = new("tcp://127.0.0.1:21000")
                    },
                    [new NodeActorHostDescriptor("room", "policy", "build")],
                    [],
                    labels: null))
                .ToArray();
            return new ValueTask<IReadOnlyList<ClusterNodeDescriptor>>(records);
        }

        public ValueTask<IReadOnlyList<ClusterNodeDescriptor>> ListAsync(
            IReadOnlyDictionary<string, string> labels,
            CancellationToken cancellationToken = default) =>
            QueryAsync(new ClusterNodeDiscoveryQuery(labels: labels), cancellationToken);

        public async ValueTask<ClusterNodeDescriptor?> AnyAsync(
            IReadOnlyDictionary<string, string> labels,
            CancellationToken cancellationToken = default) =>
            (await ListAsync(labels, cancellationToken)).FirstOrDefault();
    }

    private sealed class FixedHotfixRuntimeAccessor(HotfixRuntimeSnapshot snapshot) : IHotfixRuntimeAccessor
    {
        public HotfixRuntimeSnapshot Current => snapshot;
    }
}
