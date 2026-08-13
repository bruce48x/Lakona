using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Tests.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Server.Tests.Actors;

public sealed class ActorLifecycleRpcHandlerTests
{
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
                ActivationId = Guid.Parse("94000000-0000-0000-0000-000000000000"),
                ActivationVersion = current.Record.Version - 1
            },
            cancellationToken);

        Assert.True(reply.Succeeded);
        Assert.Equal(currentActivation, (await directory.ResolveAsync(actorId, cancellationToken))!.ActivationId);
    }
}
