using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Actors;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Tests.Testing;
using Microsoft.Extensions.DependencyInjection;
using MemoryPack;
using Xunit;

namespace Lakona.Game.Server.Tests.Cluster.Actors;

public sealed class ActorLifecycleRpcHandlerTests
{
    [Fact]
    public void Create_and_destroy_rpc_methods_have_distinct_request_contracts()
    {
        var createRequest = ActorLifecycleProtocol.Create
            .GetType()
            .GetGenericArguments()[0];
        var destroyRequest = ActorLifecycleProtocol.Destroy
            .GetType()
            .GetGenericArguments()[0];

        Assert.Equal(typeof(ActorLifecycleCreateRequest), createRequest);
        Assert.Equal(typeof(ActorLifecycleDestroyRequest), destroyRequest);
        Assert.NotEqual(createRequest, destroyRequest);
    }

    [Fact]
    public void Create_and_destroy_wire_contracts_roundtrip_their_distinct_shapes()
    {
        var target = new ActorLifecycleWireTarget
        {
            ActorId = "room/wire",
            ClusterIncarnation = Guid.Parse("B1000000-0000-0000-0000-000000000000"),
            Node = "battle-1",
            NodeIncarnation = Guid.Parse("B2000000-0000-0000-0000-000000000000"),
            ActivationId = Guid.Parse("B3000000-0000-0000-0000-000000000000")
        };
        var create = new ActorLifecycleCreateRequest
        {
            Actor = "room",
            Mode = ActorPlacementCreateMode.Ensure,
            HotfixVersion = "build-1",
            Target = target
        };
        var destroy = new ActorLifecycleDestroyRequest
        {
            Actor = "room",
            Target = target
        };

        var decodedCreate = MemoryPackSerializer.Deserialize<ActorLifecycleCreateRequest>(
            MemoryPackSerializer.Serialize(create));
        var decodedDestroy = MemoryPackSerializer.Deserialize<ActorLifecycleDestroyRequest>(
            MemoryPackSerializer.Serialize(destroy));

        Assert.NotNull(decodedCreate);
        Assert.Equal(ActorPlacementCreateMode.Ensure, decodedCreate.Mode);
        Assert.Equal("build-1", decodedCreate.HotfixVersion);
        Assert.Equal(target.ActivationId, decodedCreate.Target.ActivationId);
        Assert.NotNull(decodedDestroy);
        Assert.Equal(target.ActorId, decodedDestroy.Target.ActorId);
        Assert.Equal(target.Node, decodedDestroy.Target.Node);
    }

    [Fact]
    public async Task Create_and_destroy_dispatch_their_fixed_typed_operations()
    {
        await using var fixture = await CreateFixtureAsync<LifecycleActor>("current-build");
        var cancellationToken = TestContext.Current.CancellationToken;

        var created = await fixture.Handler.HandleCreateAsync(
            fixture.CreateRequest("room", ActorPlacementCreateMode.Create),
            cancellationToken);

        Assert.True(created.Succeeded, created.Message);
        Assert.Equal("create", created.Message);
        Assert.Contains(
            fixture.ActorId,
            fixture.Provider.GetRequiredService<IActorRuntime>()
                .GetActiveActorIds(typeof(LifecycleActor)));

        var destroyed = await fixture.Handler.HandleDestroyAsync(
            fixture.DestroyRequest("room"),
            cancellationToken);

        Assert.True(destroyed.Succeeded, destroyed.Message);
        Assert.Equal("destroy", destroyed.Message);
        Assert.DoesNotContain(
            fixture.ActorId,
            fixture.Provider.GetRequiredService<IActorRuntime>()
                .GetActiveActorIds(typeof(LifecycleActor)));
    }

    [Fact]
    public async Task Create_rejects_unknown_mode_and_malformed_target_before_hosting()
    {
        await using var fixture = await CreateFixtureAsync<LifecycleActor>("current-build");
        var cancellationToken = TestContext.Current.CancellationToken;
        var invalidMode = fixture.CreateRequest(
            "room",
            (ActorPlacementCreateMode)int.MaxValue);
        var malformedTarget = fixture.CreateRequest("room", ActorPlacementCreateMode.Create);
        malformedTarget.Target = null!;

        var invalidModeReply = await fixture.Handler.HandleCreateAsync(
            invalidMode,
            cancellationToken);
        var malformedReply = await fixture.Handler.HandleCreateAsync(
            malformedTarget,
            cancellationToken);

        Assert.False(invalidModeReply.Succeeded);
        Assert.False(malformedReply.Succeeded);
        Assert.Empty(
            fixture.Provider.GetRequiredService<IActorRuntime>()
                .GetActiveActorIds(typeof(LifecycleActor)));
    }

    [Theory]
    [InlineData("actor_id")]
    [InlineData("cluster")]
    [InlineData("node")]
    [InlineData("incarnation")]
    [InlineData("activation")]
    public async Task Create_rejects_each_incomplete_exact_target_field(
        string invalidField)
    {
        await using var fixture = await CreateFixtureAsync<LifecycleActor>("current-build");
        var request = fixture.CreateRequest("room", ActorPlacementCreateMode.Create);
        switch (invalidField)
        {
            case "actor_id":
                request.Target.ActorId = " ";
                break;
            case "cluster":
                request.Target.ClusterIncarnation = Guid.Empty;
                break;
            case "node":
                request.Target.Node = " ";
                break;
            case "incarnation":
                request.Target.NodeIncarnation = Guid.Empty;
                break;
            case "activation":
                request.Target.ActivationId = Guid.Empty;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(invalidField));
        }

        var reply = await fixture.Handler.HandleCreateAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.False(reply.Succeeded);
        Assert.Empty(
            fixture.Provider.GetRequiredService<IActorRuntime>()
                .GetActiveActorIds(typeof(LifecycleActor)));
    }

    [Fact]
    public async Task Lifecycle_dispatch_catalog_tracks_the_current_hotfix_snapshot()
    {
        await using var fixture = await CreateFixtureAsync<LifecycleActor>("current-build");
        fixture.Accessor.Current = CreateSnapshot<ReloadedLifecycleActor>(
            fixture.Provider,
            "reloaded-build");
        var cancellationToken = TestContext.Current.CancellationToken;

        var stale = await fixture.Handler.HandleCreateAsync(
            fixture.CreateRequest(
                "room",
                ActorPlacementCreateMode.Create,
                "reloaded-build"),
            cancellationToken);
        var current = await fixture.Handler.HandleCreateAsync(
            fixture.CreateRequest(
                "battle",
                ActorPlacementCreateMode.Create,
                "reloaded-build"),
            cancellationToken);

        Assert.False(stale.Succeeded);
        Assert.Contains("not loaded", stale.Message, StringComparison.Ordinal);
        Assert.True(current.Succeeded, current.Message);
        Assert.Contains(
            fixture.ActorId,
            fixture.Provider.GetRequiredService<IActorRuntime>()
                .GetActiveActorIds(typeof(ReloadedLifecycleActor)));
    }

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
        var localIdentity = new LocalActorNodeIdentity(node);
        localIdentity.Observe(owner);
        var handler = new ActorLifecycleRpcHandler(
            activationCatalog: null!,
            directory,
            new FixedHotfixRuntimeAccessor(snapshot),
            localIdentity,
            services);

        var reply = await handler.HandleCreateAsync(
            new ActorLifecycleCreateRequest
            {
                Actor = "room",
                Mode = ActorPlacementCreateMode.Create,
                HotfixVersion = "obsolete-build",
                Target = new ActorLifecycleWireTarget
                {
                    ActorId = actorId.Value,
                    ClusterIncarnation = cluster.Value,
                    Node = node.Value,
                    NodeIncarnation = owner.Incarnation.Value,
                    ActivationId = activation.Value
                }
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
        var localIdentity = new LocalActorNodeIdentity(node);
        localIdentity.Observe(owner);
        var handler = new ActorLifecycleRpcHandler(
            activationCatalog: null!,
            directory,
            hotfixRuntime: null!,
            localIdentity,
            services);

        var reply = await handler.HandleDestroyAsync(
            new ActorLifecycleDestroyRequest
            {
                Actor = "room",
                Target = new ActorLifecycleWireTarget
                {
                    ActorId = actorId.Value,
                    ClusterIncarnation = cluster.Value,
                    Node = node.Value,
                    NodeIncarnation = owner.Incarnation.Value,
                    ActivationId = Guid.Parse("94000000-0000-0000-0000-000000000000")
                }
            },
            cancellationToken);

        Assert.True(reply.Succeeded);
        Assert.Equal(currentActivation, (await directory.ResolveAsync(actorId, cancellationToken))!.ActivationId);
    }

    [Fact]
    public async Task Ensure_reports_the_current_owner_when_its_exact_proposal_loses()
    {
        await using var fixture = await CreateFixtureAsync<LifecycleActor>("current-build");
        var cancellationToken = TestContext.Current.CancellationToken;
        var currentActivation = ActorActivationId.New();
        await fixture.Directory.AcquireAsync(
            fixture.ActorId,
            fixture.Owner,
            currentActivation,
            cancellationToken);

        var reply = await fixture.Handler.HandleCreateAsync(
            fixture.CreateRequest("room", ActorPlacementCreateMode.Ensure),
            cancellationToken);

        Assert.False(reply.Succeeded);
        Assert.Equal(fixture.Owner.Node.Value, reply.OwnerNode);
        Assert.Equal(
            currentActivation,
            (await fixture.Directory.ResolveAsync(fixture.ActorId, cancellationToken))!.ActivationId);
    }

    private static async ValueTask<LifecycleFixture> CreateFixtureAsync<TActor>(
        string hotfixVersion)
        where TActor : class, IActor
    {
        var cluster = new ClusterIncarnationId(
            Guid.Parse("A1000000-0000-0000-0000-000000000000"));
        var owner = new NodeReference(
            cluster,
            new NodeId("battle-1"),
            new NodeIncarnationId(
                Guid.Parse("A2000000-0000-0000-0000-000000000000")));
        var actorId = ActorId.From("room/lifecycle-dispatch");
        var activation = new ActorActivationId(
            Guid.Parse("A3000000-0000-0000-0000-000000000000"));
        var directory = new TestActorDirectory();
        var services = new ServiceCollection()
            .AddSingleton(new LocalActorNodeIdentity(owner.Node))
            .AddLakonaGameServerActors()
            .AddSingleton<IActorDirectory>(directory)
            .AddSingleton<IActorDirectoryCache, InMemoryActorDirectoryCache>();
        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<LocalActorNodeIdentity>().Observe(owner);
        var accessor = new FixedHotfixRuntimeAccessor(
            CreateSnapshot<TActor>(provider, hotfixVersion));
        var handler = new ActorLifecycleRpcHandler(
            provider.GetRequiredService<ActorActivationCatalog>(),
            directory,
            accessor,
            provider.GetRequiredService<LocalActorNodeIdentity>(),
            provider);
        return new LifecycleFixture(
            provider,
            handler,
            accessor,
            actorId,
            owner,
            activation,
            hotfixVersion);
    }

    private static HotfixRuntimeSnapshot CreateSnapshot<TActor>(
        IServiceProvider services,
        string hotfixVersion)
        where TActor : class, IActor =>
        new(
            new HotfixServiceInvoker(new HotfixDispatchTable(1, [], [])),
            services,
            actorStartups: [],
            actorPlacements:
            [
                ActorPlacementDeclaration.Create<TActor, ActorId>(
                    static context => context.Candidates[0])
            ],
            sourceVersion: hotfixVersion);

    [ActorName("room")]
    private sealed class LifecycleActor : Lakona.Game.Server.Actors.Actor;

    [ActorName("battle")]
    private sealed class ReloadedLifecycleActor : Lakona.Game.Server.Actors.Actor;

    private sealed class FixedHotfixRuntimeAccessor(HotfixRuntimeSnapshot snapshot) : IHotfixRuntimeAccessor
    {
        public HotfixRuntimeSnapshot Current { get; set; } = snapshot;
    }

    private sealed class LifecycleFixture(
        ServiceProvider provider,
        ActorLifecycleRpcHandler handler,
        FixedHotfixRuntimeAccessor accessor,
        ActorId actorId,
        NodeReference owner,
        ActorActivationId activation,
        string hotfixVersion) : IAsyncDisposable
    {
        public ServiceProvider Provider { get; } = provider;
        public ActorLifecycleRpcHandler Handler { get; } = handler;
        public FixedHotfixRuntimeAccessor Accessor { get; } = accessor;
        public ActorId ActorId { get; } = actorId;
        public TestActorDirectory Directory { get; } =
            (TestActorDirectory)provider.GetRequiredService<IActorDirectory>();
        public NodeReference Owner { get; } = owner;

        public ActorLifecycleCreateRequest CreateRequest(
            string actor,
            ActorPlacementCreateMode mode,
            string? requestedHotfixVersion = null) => new()
        {
            Actor = actor,
            Mode = mode,
            HotfixVersion = requestedHotfixVersion ?? hotfixVersion,
            Target = Target()
        };

        public ActorLifecycleDestroyRequest DestroyRequest(string actor) => new()
        {
            Actor = actor,
            Target = Target()
        };

        public ValueTask DisposeAsync() => Provider.DisposeAsync();

        private ActorLifecycleWireTarget Target() => new()
        {
            ActorId = ActorId.Value,
            ClusterIncarnation = Owner.Cluster.Value,
            Node = Owner.Node.Value,
            NodeIncarnation = Owner.Incarnation.Value,
            ActivationId = activation.Value
        };
    }
}
