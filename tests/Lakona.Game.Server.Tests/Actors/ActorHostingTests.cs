using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using GameActor = Lakona.Game.Server.Actors.Actor;

namespace Lakona.Game.Server.Tests.Actors;

public sealed class ActorHostingTests
{
    private static readonly NodeId LocalNode = new("node-a");
    private static readonly NodeId RemoteNode = new("node-b");

    [Fact]
    public async Task CreateAsync_registers_directory_creates_local_actor_and_sets_cache()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider();
        var hosting = provider.GetRequiredService<ActorHosting>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var directory = provider.GetRequiredService<IActorDirectory>();
        var cache = provider.GetRequiredService<IActorDirectoryCache>();
        var actorId = ActorId.From("hosting/create");

        await hosting.CreateAsync<HostedTestActor>(actorId, cancellationToken);

        var record = await directory.ResolveAsync(actorId, cancellationToken);
        Assert.NotNull(record);
        Assert.Equal(LocalNode, record.Node);
        Assert.True(cache.TryGet(actorId, out var cachedNode));
        Assert.Equal(LocalNode, cachedNode);

        var activated = await runtime.AskAsync<HostedTestActor, int>(
            actorId,
            static async (actor, _) => await actor.GetActivatedCountAsync(),
            cancellationToken);
        Assert.Equal(1, activated);
    }

    [Fact]
    public async Task CreateAsync_skips_directory_and_cache_for_local_only_actor()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider();
        var hosting = provider.GetRequiredService<ActorHosting>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var directory = provider.GetRequiredService<IActorDirectory>();
        var cache = provider.GetRequiredService<IActorDirectoryCache>();
        var actorId = ActorId.From("hosting/local-only");

        await hosting.CreateAsync<LocalOnlyHostedTestActor>(actorId, cancellationToken);

        Assert.Null(await directory.ResolveAsync(actorId, cancellationToken));
        Assert.False(cache.TryGet(actorId, out _));
        Assert.Equal(
            1,
            await runtime.AskAsync<LocalOnlyHostedTestActor, int>(
                actorId,
                static async (actor, _) => await actor.GetActivatedCountAsync(),
                cancellationToken));
    }

    [Fact]
    public async Task CreateAsync_fails_when_same_actor_is_already_active_locally()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider();
        var hosting = provider.GetRequiredService<ActorHosting>();
        var actorId = ActorId.From("hosting/already-active");

        await hosting.CreateAsync<HostedTestActor>(actorId, cancellationToken);

        await Assert.ThrowsAsync<ActorAlreadyHostedException>(async () =>
            await hosting.CreateAsync<HostedTestActor>(actorId, cancellationToken));
    }

    [Fact]
    public async Task CreateAsync_fails_when_directory_route_belongs_to_another_node()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider();
        var hosting = provider.GetRequiredService<ActorHosting>();
        var directory = provider.GetRequiredService<IActorDirectory>();
        var cache = provider.GetRequiredService<IActorDirectoryCache>();
        var actorId = ActorId.From("hosting/remote-owned");
        await directory.RegisterAsync(actorId, RemoteNode, cancellationToken);

        await Assert.ThrowsAsync<ActorHostedElsewhereException>(async () =>
            await hosting.CreateAsync<HostedTestActor>(actorId, cancellationToken));

        var record = await directory.ResolveAsync(actorId, cancellationToken);
        Assert.NotNull(record);
        Assert.Equal(RemoteNode, record.Node);
        Assert.False(cache.TryGet(actorId, out _));
    }

    [Fact]
    public async Task CreateAsync_rolls_back_directory_cache_and_local_actor_when_local_create_fails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider();
        var hosting = provider.GetRequiredService<ActorHosting>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var directory = provider.GetRequiredService<IActorDirectory>();
        var cache = provider.GetRequiredService<IActorDirectoryCache>();
        var actorId = ActorId.From("hosting/create-fails");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await hosting.CreateAsync<FailingActivationActor>(actorId, cancellationToken));

        Assert.Null(await directory.ResolveAsync(actorId, cancellationToken));
        Assert.False(cache.TryGet(actorId, out _));
        Assert.DoesNotContain(actorId, runtime.GetActiveActorIds(typeof(FailingActivationActor)));
    }

    [Fact]
    public async Task EnsureAsync_returns_when_same_type_actor_is_already_active_locally()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider();
        var hosting = provider.GetRequiredService<ActorHosting>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var actorId = ActorId.From("hosting/ensure-existing");

        await hosting.CreateAsync<HostedTestActor>(actorId, cancellationToken);
        await hosting.EnsureAsync<HostedTestActor>(actorId, cancellationToken);

        Assert.Equal(
            1,
            await runtime.AskAsync<HostedTestActor, int>(
                actorId,
                static async (actor, _) => await actor.GetActivatedCountAsync(),
                cancellationToken));
    }

    [Fact]
    public async Task EnsureAsync_creates_when_actor_is_absent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider();
        var hosting = provider.GetRequiredService<ActorHosting>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var actorId = ActorId.From("hosting/ensure-creates");

        await hosting.EnsureAsync<HostedTestActor>(actorId, cancellationToken);

        Assert.Contains(actorId, runtime.GetActiveActorIds(typeof(HostedTestActor)));
    }

    [Fact]
    public async Task EnsureAsync_fails_when_local_actor_type_mismatches()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider();
        var hosting = provider.GetRequiredService<ActorHosting>();
        var actorId = ActorId.From("hosting/type-mismatch");

        await hosting.CreateAsync<HostedTestActor>(actorId, cancellationToken);

        await Assert.ThrowsAsync<ActorHostingTypeMismatchException>(async () =>
            await hosting.EnsureAsync<OtherHostedTestActor>(actorId, cancellationToken));
    }

    [Fact]
    public async Task EnsureAsync_fails_and_clears_stale_cache_when_directory_owner_is_remote()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider();
        var hosting = provider.GetRequiredService<ActorHosting>();
        var directory = provider.GetRequiredService<IActorDirectory>();
        var cache = provider.GetRequiredService<IActorDirectoryCache>();
        var actorId = ActorId.From("hosting/stale-cache");

        await directory.RegisterAsync(actorId, RemoteNode, cancellationToken);
        cache.Set(actorId, LocalNode);

        await Assert.ThrowsAsync<ActorHostedElsewhereException>(async () =>
            await hosting.EnsureAsync<HostedTestActor>(actorId, cancellationToken));

        Assert.False(cache.TryGet(actorId, out _));
    }

    [Fact]
    public async Task DestroyAsync_unregisters_local_route_clears_cache_and_stops_actor()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider();
        var hosting = provider.GetRequiredService<ActorHosting>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var directory = provider.GetRequiredService<IActorDirectory>();
        var cache = provider.GetRequiredService<IActorDirectoryCache>();
        var actorId = ActorId.From("hosting/destroy");

        await hosting.CreateAsync<HostedTestActor>(actorId, cancellationToken);
        await hosting.DestroyAsync<HostedTestActor>(actorId, cancellationToken);

        Assert.Null(await directory.ResolveAsync(actorId, cancellationToken));
        Assert.False(cache.TryGet(actorId, out _));
        Assert.DoesNotContain(actorId, runtime.GetActiveActorIds(typeof(HostedTestActor)));
    }

    [Fact]
    public async Task DestroyAsync_is_idempotent_when_actor_and_route_are_absent()
    {
        await using var provider = CreateProvider();
        var hosting = provider.GetRequiredService<ActorHosting>();

        await hosting.DestroyAsync<HostedTestActor>(
            ActorId.From("hosting/missing"),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DestroyAsync_preserves_remote_route_but_removes_stale_local_cache_and_cell()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider();
        var hosting = provider.GetRequiredService<ActorHosting>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var directory = provider.GetRequiredService<IActorDirectory>();
        var cache = provider.GetRequiredService<IActorDirectoryCache>();
        var actorId = ActorId.From("hosting/remote-preserved");

        await hosting.CreateAsync<HostedTestActor>(actorId, cancellationToken);
        await directory.UnregisterAsync(actorId, LocalNode, cancellationToken);
        await directory.RegisterAsync(actorId, RemoteNode, cancellationToken);
        cache.Set(actorId, LocalNode);

        await hosting.DestroyAsync<HostedTestActor>(actorId, cancellationToken);

        var record = await directory.ResolveAsync(actorId, cancellationToken);
        Assert.NotNull(record);
        Assert.Equal(RemoteNode, record.Node);
        Assert.False(cache.TryGet(actorId, out _));
        Assert.DoesNotContain(actorId, runtime.GetActiveActorIds(typeof(HostedTestActor)));
    }

    [Fact]
    public async Task DestroyAsync_restores_local_route_and_cache_when_stop_times_out_or_fails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider(options => options.CallTimeout = TimeSpan.FromMilliseconds(20));
        var hosting = provider.GetRequiredService<ActorHosting>();
        var directory = provider.GetRequiredService<IActorDirectory>();
        var cache = provider.GetRequiredService<IActorDirectoryCache>();
        var actorId = ActorId.From("hosting/stop-timeout");

        await hosting.CreateAsync<BlockingDeactivateActor>(actorId, cancellationToken);

        await Assert.ThrowsAsync<ActorHostingStopException>(async () =>
            await hosting.DestroyAsync<BlockingDeactivateActor>(actorId, cancellationToken));

        var record = await directory.ResolveAsync(actorId, cancellationToken);
        Assert.NotNull(record);
        Assert.Equal(LocalNode, record.Node);
        Assert.True(cache.TryGet(actorId, out var cachedNode));
        Assert.Equal(LocalNode, cachedNode);
        BlockingDeactivateActor.ReleaseAll();
    }

    [Fact]
    public async Task CreateEnsureDestroy_for_same_actor_id_are_serialized()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var directory = new DelayingActorDirectory();
        await using var provider = CreateProvider(directory: directory);
        var hosting = provider.GetRequiredService<ActorHosting>();
        var actorId = ActorId.From("hosting/serialized");

        var create = hosting.CreateAsync<HostedTestActor>(actorId, cancellationToken).AsTask();
        var destroy = hosting.DestroyAsync<HostedTestActor>(actorId, cancellationToken).AsTask();

        await Task.WhenAll(create, destroy);

        Assert.Equal(1, directory.MaxConcurrentOperations);
    }

    private static ServiceProvider CreateProvider(
        Action<ActorRuntimeOptions>? configure = null,
        IActorDirectory? directory = null)
    {
        var services = new ServiceCollection()
            .AddSingleton(new LocalActorNodeIdentity(LocalNode))
            .AddLakonaGameServerActors(configure);

        if (directory is not null)
        {
            services.AddSingleton(directory);
        }

        return services.BuildServiceProvider();
    }

    private class HostedTestActor : GameActor
    {
        private int _activatedCount;

        protected override ValueTask OnActivateAsync(CancellationToken cancellationToken)
        {
            _activatedCount++;
            return default;
        }

        public async ValueTask<int> GetActivatedCountAsync()
        {
            await Task.Yield();
            return _activatedCount;
        }
    }

    private sealed class OtherHostedTestActor : GameActor;

    [ActorLocalOnly]
    private sealed class LocalOnlyHostedTestActor : HostedTestActor;

    private sealed class FailingActivationActor : GameActor
    {
        protected override ValueTask OnActivateAsync(CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("activation failed");
        }
    }

    private sealed class BlockingDeactivateActor : GameActor
    {
        private static TaskCompletionSource _release = NewRelease();

        protected override async ValueTask OnDeactivateAsync(CancellationToken cancellationToken)
        {
            await _release.Task.WaitAsync(cancellationToken);
        }

        public static void ReleaseAll()
        {
            _release.TrySetResult();
            _release = NewRelease();
        }

        private static TaskCompletionSource NewRelease()
        {
            return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    private sealed class DelayingActorDirectory : IActorDirectory
    {
        private readonly InMemoryActorDirectory _inner = new();
        private int _currentOperations;

        public int MaxConcurrentOperations { get; private set; }

        public async ValueTask<ActorDirectoryRecord?> ResolveAsync(
            ActorId actorId,
            CancellationToken cancellationToken = default)
        {
            using var _ = await EnterAsync(cancellationToken);
            return await _inner.ResolveAsync(actorId, cancellationToken);
        }

        public async ValueTask<ActorDirectoryRegisterStatus> RegisterAsync(
            ActorId actorId,
            NodeId node,
            CancellationToken cancellationToken = default)
        {
            using var _ = await EnterAsync(cancellationToken);
            return await _inner.RegisterAsync(actorId, node, cancellationToken);
        }

        public async ValueTask<ActorDirectoryUnregisterStatus> UnregisterAsync(
            ActorId actorId,
            NodeId node,
            CancellationToken cancellationToken = default)
        {
            using var _ = await EnterAsync(cancellationToken);
            return await _inner.UnregisterAsync(actorId, node, cancellationToken);
        }

        private async ValueTask<IDisposable> EnterAsync(CancellationToken cancellationToken)
        {
            var current = Interlocked.Increment(ref _currentOperations);
            MaxConcurrentOperations = Math.Max(MaxConcurrentOperations, current);
            await Task.Delay(25, cancellationToken);
            return new Releaser(this);
        }

        private sealed class Releaser(DelayingActorDirectory directory) : IDisposable
        {
            public void Dispose()
            {
                Interlocked.Decrement(ref directory._currentOperations);
            }
        }
    }
}
