using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Actors;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Tests.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Server.Tests.Cluster.Actors;

public sealed class ActorLifecycleRpcHandlerTests
{
    [Fact]
    public async Task Create_rejects_a_capability_from_an_obsolete_hotfix_build()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var actorId = ActorId.From("room/stale-build");
        var cluster = new ClusterIncarnationId(Guid.Parse("81000000-0000-0000-0000-000000000000"));
        var node = new NodeId("battle-1");
        var owner = new NodeReference(
            cluster,
            node,
            new NodeIncarnationId(Guid.Parse("82000000-0000-0000-0000-000000000000")));
        var activation = new ActorActivationId(Guid.Parse("83000000-0000-0000-0000-000000000000"));
        var directory = new TestActorDirectory();
        await directory.AcquireAsync(actorId, owner, activation, cancellationToken);
        using var services = new ServiceCollection().BuildServiceProvider();
        var snapshot = new HotfixRuntimeSnapshot(
            new HotfixServiceInvoker(new HotfixDispatchTable(1, [], [])),
            services,
            actorStartups: [],
            sourceVersion: "current-build");
        var handler = new ActorLifecycleRpcHandler(
            hosting: null!,
            directory,
            new FixedHotfixRuntimeAccessor(snapshot),
            new LocalActorNodeIdentity(node),
            services);

        var reply = await handler.HandleAsync(
            new ActorLifecycleRequest
            {
                Actor = "room",
                ActorId = actorId.Value,
                Mode = "create",
                BuildTag = "obsolete-build",
                ClusterIncarnation = cluster.Value,
                NodeIncarnation = owner.Incarnation.Value,
                ActivationId = activation.Value
            },
            cancellationToken);

        Assert.False(reply.Succeeded);
        Assert.Contains("stale", reply.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("current-build", reply.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Delayed_destroy_for_replaced_activation_is_idempotent_without_touching_replacement()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var actorId = ActorId.From("room/delayed-destroy");
        var cluster = new ClusterIncarnationId(Guid.Parse("91000000-0000-0000-0000-000000000000"));
        var node = new NodeId("battle-1");
        var owner = new NodeReference(
            cluster,
            node,
            new NodeIncarnationId(Guid.Parse("92000000-0000-0000-0000-000000000000")));
        var currentActivation = new ActorActivationId(Guid.Parse("93000000-0000-0000-0000-000000000000"));
        var directory = new TestActorDirectory();
        var current = await directory.AcquireAsync(actorId, owner, currentActivation, cancellationToken);
        using var services = new ServiceCollection().BuildServiceProvider();
        var handler = new ActorLifecycleRpcHandler(
            hosting: null!,
            directory,
            hotfixRuntime: null!,
            new LocalActorNodeIdentity(node),
            services);

        var reply = await handler.HandleAsync(
            new ActorLifecycleRequest
            {
                Actor = "room",
                ActorId = actorId.Value,
                Mode = "destroy",
                ClusterIncarnation = cluster.Value,
                NodeIncarnation = owner.Incarnation.Value,
                ActivationId = Guid.Parse("94000000-0000-0000-0000-000000000000")
            },
            cancellationToken);

        Assert.True(reply.Succeeded);
        Assert.Equal(currentActivation, (await directory.ResolveAsync(actorId, cancellationToken))!.ActivationId);
    }

    private sealed class FixedHotfixRuntimeAccessor(HotfixRuntimeSnapshot snapshot) : IHotfixRuntimeAccessor
    {
        public HotfixRuntimeSnapshot Current => snapshot;
    }
}
