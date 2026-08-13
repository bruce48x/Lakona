using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Xunit;

namespace Lakona.Game.Server.Tests.Actors;

public sealed class ActorPlacementTests
{
    [Fact]
    public async Task Stable_selector_delegates_lifecycle_operations_with_canonical_identity()
    {
        var service = new RecordingPlacementService();
        var key = new RoomId("room/one");
        var placement = new ActorPlacement<RoomActor, RoomId>(service, key);

        await placement.CreateAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ActorPlacementCreateMode.Create, service.CreateMode);
        Assert.Equal(key, service.Key);
        Assert.Equal(ActorId.From("room/room%2Fone"), service.ActorId);

        await placement.EnsureAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ActorPlacementCreateMode.Ensure, service.CreateMode);

        await placement.DestroyAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ActorId.From("room/room%2Fone"), service.DestroyedActorId);
    }

    private readonly record struct RoomId(string Value);

    [ActorName("room")]
    private sealed class RoomActor : Actor<RoomId>;

    private sealed class RecordingPlacementService : IActorPlacementService
    {
        public ActorId? ActorId { get; private set; }

        public RoomId? Key { get; private set; }

        public ActorPlacementCreateMode? CreateMode { get; private set; }

        public ActorId? DestroyedActorId { get; private set; }

        public ValueTask<ActorPlacementResult> PlaceAsync<TActor, TKey>(
            TKey key,
            ActorPlacementCreateMode createMode,
            CancellationToken cancellationToken = default)
            where TActor : class, IActor
            where TKey : notnull
        {
            return PlaceAsync<TActor, TKey>(
                ActorIdentity.Create<TActor, TKey>(key),
                key,
                createMode,
                cancellationToken);
        }

        public ValueTask<ActorPlacementResult> PlaceAsync<TActor, TKey>(
            ActorId actorId,
            TKey key,
            ActorPlacementCreateMode createMode,
            CancellationToken cancellationToken = default)
            where TActor : class, IActor
            where TKey : notnull
        {
            ActorId = actorId;
            Key = Assert.IsType<RoomId>(key);
            CreateMode = createMode;
            return new ValueTask<ActorPlacementResult>(
                new ActorPlacementResult(actorId, new NodeId("node-1")));
        }

        public ValueTask DestroyAsync<TActor>(
            ActorId actorId,
            CancellationToken cancellationToken = default)
            where TActor : class, IActor
        {
            DestroyedActorId = actorId;
            return default;
        }
    }
}
