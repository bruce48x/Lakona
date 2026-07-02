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
    public async Task EnsureAsync_and_DestroyAsync_require_exact_actor_type_for_existing_local_actor()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider();
        var hosting = provider.GetRequiredService<ActorHosting>();
        var directory = provider.GetRequiredService<IActorDirectory>();
        var cache = provider.GetRequiredService<IActorDirectoryCache>();
        var actorId = ActorId.From("hosting/exact-type");

        await hosting.CreateAsync<LocalOnlyHostedTestActor>(actorId, cancellationToken);

        await Assert.ThrowsAsync<ActorHostingTypeMismatchException>(async () =>
            await hosting.EnsureAsync<HostedTestActor>(actorId, cancellationToken));
        await Assert.ThrowsAsync<ActorHostingTypeMismatchException>(async () =>
            await hosting.DestroyAsync<HostedTestActor>(actorId, cancellationToken));

        Assert.Null(await directory.ResolveAsync(actorId, cancellationToken));
        Assert.False(cache.TryGet(actorId, out _));
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
    public async Task DestroyAsync_does_not_restore_local_route_when_deactivation_times_out_but_kernel_stop_drains()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider(options => options.CallTimeout = TimeSpan.FromMilliseconds(20));
        var hosting = provider.GetRequiredService<ActorHosting>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var directory = provider.GetRequiredService<IActorDirectory>();
        var cache = provider.GetRequiredService<IActorDirectoryCache>();
        var actorId = ActorId.From("hosting/stop-timeout");

        await hosting.CreateAsync<BlockingDeactivateActor>(actorId, cancellationToken);
        await hosting.DestroyAsync<BlockingDeactivateActor>(actorId, cancellationToken);

        Assert.Null(await directory.ResolveAsync(actorId, cancellationToken));
        Assert.False(cache.TryGet(actorId, out _));
        Assert.DoesNotContain(actorId, runtime.GetActiveActorIds(typeof(BlockingDeactivateActor)));
        Assert.Equal(ActorState.Dead, runtime.GetState(actorId));
    }

    [Fact]
    public async Task DestroyAsync_does_not_restore_local_cache_when_remote_owner_appears_after_stop_timeout()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var directory = new RemoteOwnerAfterLocalUnregisterDirectory(RemoteNode);
        await using var provider = CreateProvider(
            options => options.CallTimeout = TimeSpan.FromMilliseconds(20),
            directory);
        var hosting = provider.GetRequiredService<ActorHosting>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var cache = provider.GetRequiredService<IActorDirectoryCache>();
        var actorId = ActorId.From("hosting/timeout-remote-steals");

        await hosting.CreateAsync<BlockingDeactivateActor>(actorId, cancellationToken);
        await hosting.DestroyAsync<BlockingDeactivateActor>(actorId, cancellationToken);

        var record = await directory.ResolveAsync(actorId, cancellationToken);
        Assert.NotNull(record);
        Assert.Equal(RemoteNode, record.Node);
        Assert.False(cache.TryGet(actorId, out _));
        Assert.DoesNotContain(actorId, runtime.GetActiveActorIds(typeof(BlockingDeactivateActor)));
        Assert.Equal(ActorState.Dead, runtime.GetState(actorId));
    }

    [Fact]
    public async Task DestroyAsync_restores_local_route_cache_and_preserves_actor_when_deactivation_throws()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider();
        var hosting = provider.GetRequiredService<ActorHosting>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var directory = provider.GetRequiredService<IActorDirectory>();
        var cache = provider.GetRequiredService<IActorDirectoryCache>();
        var actorId = ActorId.From("hosting/deactivate-throws");

        await hosting.CreateAsync<ThrowingDeactivateActor>(actorId, cancellationToken);

        var exception = await Assert.ThrowsAsync<ActorHostingStopException>(async () =>
            await hosting.DestroyAsync<ThrowingDeactivateActor>(actorId, cancellationToken));

        Assert.IsType<InvalidOperationException>(exception.InnerException);
        var record = await directory.ResolveAsync(actorId, cancellationToken);
        Assert.NotNull(record);
        Assert.Equal(LocalNode, record.Node);
        Assert.True(cache.TryGet(actorId, out var cachedNode));
        Assert.Equal(LocalNode, cachedNode);
        Assert.Contains(actorId, runtime.GetActiveActorIds(typeof(ThrowingDeactivateActor)));
        Assert.Equal(ActorState.Active, runtime.GetState(actorId));
    }

    [Fact]
    public async Task DestroyAsync_does_not_restore_local_cache_when_remote_owner_appears_after_stop_exception()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var directory = new RemoteOwnerAfterLocalUnregisterDirectory(RemoteNode);
        await using var provider = CreateProvider(directory: directory);
        var hosting = provider.GetRequiredService<ActorHosting>();
        var cache = provider.GetRequiredService<IActorDirectoryCache>();
        var actorId = ActorId.From("hosting/throw-remote-steals");

        await hosting.CreateAsync<ThrowingDeactivateActor>(actorId, cancellationToken);

        await Assert.ThrowsAsync<ActorHostingStopException>(async () =>
            await hosting.DestroyAsync<ThrowingDeactivateActor>(actorId, cancellationToken));

        var record = await directory.ResolveAsync(actorId, cancellationToken);
        Assert.NotNull(record);
        Assert.Equal(RemoteNode, record.Node);
        Assert.False(cache.TryGet(actorId, out _));
    }

    [Fact]
    public async Task EnsureAsync_throws_directory_unavailable_and_clears_cache_when_conflict_has_no_owner()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var directory = new ConflictWithoutOwnerOnNextRegisterDirectory();
        await using var provider = CreateProvider(directory: directory);
        var hosting = provider.GetRequiredService<ActorHosting>();
        var cache = provider.GetRequiredService<IActorDirectoryCache>();
        var actorId = ActorId.From("hosting/ensure-conflict-null");

        await hosting.CreateAsync<HostedTestActor>(actorId, cancellationToken);
        cache.Set(actorId, LocalNode);
        directory.ConflictWithoutOwnerOnNextRegister = true;

        await Assert.ThrowsAsync<ActorDirectoryUnavailableException>(async () =>
            await hosting.EnsureAsync<HostedTestActor>(actorId, cancellationToken));

        Assert.False(cache.TryGet(actorId, out _));
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

    [Fact]
    public async Task Operation_gate_rejects_references_to_retired_entries()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var gate = new ActorHostingOperationGate();
        var actorId = ActorId.From("hosting/gate-retired-entry");

        var lease = await gate.EnterAsync(actorId, cancellationToken);
        var entry = GetGateEntry(gate, actorId);
        await lease.DisposeAsync();

        Assert.False(TryAddGateEntryReference(entry));
    }

    [Fact]
    public async Task CreateAsync_does_not_destroy_preexisting_local_entry_when_runtime_create_reports_conflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var actorId = ActorId.From("hosting/create-conflict-residue");
        var runtime = new ConflictingCreateRuntime(typeof(OtherHostedTestActor));
        var hosting = new ActorHosting(
            runtime,
            new InMemoryActorDirectory(),
            new InMemoryActorDirectoryCache(),
            new LocalActorNodeIdentity(LocalNode),
            new ActorHostingRollbackRecorder());

        await Assert.ThrowsAsync<ActorHostingTypeMismatchException>(async () =>
            await hosting.CreateAsync<HostedTestActor>(actorId, cancellationToken));

        Assert.Equal(0, runtime.DestroyLocalCalls);
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

    private static object GetGateEntry(ActorHostingOperationGate gate, ActorId actorId)
    {
        var entries = typeof(ActorHostingOperationGate)
            .GetField("_entries", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(gate)!;
        var arguments = new object?[] { actorId, null };
        var found = (bool)entries.GetType().GetMethod("TryGetValue")!.Invoke(entries, arguments)!;
        Assert.True(found);
        return arguments[1]!;
    }

    private static bool TryAddGateEntryReference(object entry)
    {
        var method = entry.GetType()
            .GetMethod(
                "TryAddRef",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (bool)method.Invoke(entry, [])!;
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

    private sealed class ThrowingDeactivateActor : GameActor
    {
        protected override ValueTask OnDeactivateAsync(CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("deactivation failed");
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

    private sealed class RemoteOwnerAfterLocalUnregisterDirectory(NodeId remoteNode) : IActorDirectory
    {
        private readonly InMemoryActorDirectory _inner = new();

        public ValueTask<ActorDirectoryRecord?> ResolveAsync(
            ActorId actorId,
            CancellationToken cancellationToken = default)
        {
            return _inner.ResolveAsync(actorId, cancellationToken);
        }

        public ValueTask<ActorDirectoryRegisterStatus> RegisterAsync(
            ActorId actorId,
            NodeId node,
            CancellationToken cancellationToken = default)
        {
            return _inner.RegisterAsync(actorId, node, cancellationToken);
        }

        public async ValueTask<ActorDirectoryUnregisterStatus> UnregisterAsync(
            ActorId actorId,
            NodeId node,
            CancellationToken cancellationToken = default)
        {
            var status = await _inner.UnregisterAsync(actorId, node, cancellationToken).ConfigureAwait(false);
            if (status == ActorDirectoryUnregisterStatus.Unregistered)
            {
                await _inner.RegisterAsync(actorId, remoteNode, cancellationToken).ConfigureAwait(false);
            }

            return status;
        }
    }

    private sealed class ConflictWithoutOwnerOnNextRegisterDirectory : IActorDirectory
    {
        private readonly InMemoryActorDirectory _inner = new();

        public bool ConflictWithoutOwnerOnNextRegister { get; set; }

        public ValueTask<ActorDirectoryRecord?> ResolveAsync(
            ActorId actorId,
            CancellationToken cancellationToken = default)
        {
            return ConflictWithoutOwnerOnNextRegister
                ? new ValueTask<ActorDirectoryRecord?>((ActorDirectoryRecord?)null)
                : _inner.ResolveAsync(actorId, cancellationToken);
        }

        public ValueTask<ActorDirectoryRegisterStatus> RegisterAsync(
            ActorId actorId,
            NodeId node,
            CancellationToken cancellationToken = default)
        {
            if (ConflictWithoutOwnerOnNextRegister)
            {
                return new ValueTask<ActorDirectoryRegisterStatus>(ActorDirectoryRegisterStatus.Conflict);
            }

            return _inner.RegisterAsync(actorId, node, cancellationToken);
        }

        public ValueTask<ActorDirectoryUnregisterStatus> UnregisterAsync(
            ActorId actorId,
            NodeId node,
            CancellationToken cancellationToken = default)
        {
            return _inner.UnregisterAsync(actorId, node, cancellationToken);
        }
    }

    private sealed class ConflictingCreateRuntime(Type existingType) : IActorHostingRuntime
    {
        private bool _createAttempted;

        public int DestroyLocalCalls { get; private set; }

        public bool TryGetLocalActor(ActorId actorId, out Type actorType, out ActorState state)
        {
            actorType = _createAttempted ? existingType : typeof(IActor);
            state = _createAttempted ? ActorState.Draining : ActorState.Dead;
            return _createAttempted;
        }

        public ValueTask<ActorHostingLocalCreateResult> CreateLocalAsync(
            Type actorType,
            ActorId actorId,
            CancellationToken cancellationToken = default)
        {
            _createAttempted = true;
            return new ValueTask<ActorHostingLocalCreateResult>(new ActorHostingLocalCreateResult(
                ActorHostingLocalCreateStatus.AlreadyExistsDifferentType,
                actorId,
                actorType,
                existingType));
        }

        public ValueTask<ActorHostingLocalDestroyResult> DestroyLocalAsync(
            Type actorType,
            ActorId actorId,
            TimeSpan drainTimeout,
            CancellationToken cancellationToken = default)
        {
            DestroyLocalCalls++;
            return new ValueTask<ActorHostingLocalDestroyResult>(new ActorHostingLocalDestroyResult(
                ActorHostingLocalDestroyStatus.Destroyed,
                actorId,
                actorType));
        }
    }
}
