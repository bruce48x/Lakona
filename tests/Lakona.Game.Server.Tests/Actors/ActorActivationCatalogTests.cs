using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Actors;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Hotfix.Scanning;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using GameActor = Lakona.Game.Server.Actors.Actor;

namespace Lakona.Game.Server.Tests.Actors;

public sealed class ActorActivationCatalogTests
{
    private static readonly NodeId LocalNode = new("node-a");
    private static readonly NodeId RemoteNode = new("node-b");
    private static readonly ClusterIncarnationId TestCluster = new(
        Guid.Parse("90000000-0000-0000-0000-000000000000"));
    private static readonly NodeReference LocalReference = new(
        TestCluster,
        LocalNode,
        new NodeIncarnationId(Guid.Parse("90000001-0000-0000-0000-000000000000")));
    private static readonly NodeReference RemoteReference = new(
        TestCluster,
        RemoteNode,
        new NodeIncarnationId(Guid.Parse("90000002-0000-0000-0000-000000000000")));

    [Fact]
    public async Task CreateAsync_registers_directory_creates_local_actor_and_sets_cache()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider();
        var hosting = provider.GetRequiredService<ActorActivationCatalog>();
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
    public async Task CreateAsync_runs_actor_start_hook()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var dispatcher = new RecordingActorLifecycleDispatcher();
        await using var provider = CreateProvider(lifecycleDispatcher: dispatcher);
        var hosting = provider.GetRequiredService<ActorActivationCatalog>();
        var actorId = ActorId.From("hosting/start-hook");

        await hosting.CreateAsync<HostedTestActor>(actorId, cancellationToken);

        Assert.Equal([("start", actorId.Value, typeof(HostedTestActor))], dispatcher.Events);
    }

    [Fact]
    public async Task Activation_is_not_valid_until_actor_start_completes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var dispatcher = new BlockingStartActorLifecycleDispatcher();
        await using var provider = CreateProvider(lifecycleDispatcher: dispatcher);
        var catalog = provider.GetRequiredService<ActorActivationCatalog>();
        var actorId = ActorId.From("catalog/activating");

        var activation = catalog.CreateAsync<HostedTestActor>(actorId, cancellationToken).AsTask();
        await dispatcher.Entered.Task.WaitAsync(cancellationToken);

        Assert.Equal(ActorActivationState.Activating, catalog.GetActivationState(actorId));
        Assert.Contains(
            provider.GetRequiredService<IActorActivationSnapshotSource>().CaptureRecoveryClaims(),
            record => record.ActorId == actorId);
        await Assert.ThrowsAsync<ActorNotFoundException>(async () =>
            await catalog.AskAsync<HostedTestActor, int>(
                actorId,
                static (actor, _) => actor.GetActivatedCountAsync(),
                cancellationToken));

        dispatcher.Release.TrySetResult();
        await activation;

        Assert.Equal(ActorActivationState.Valid, catalog.GetActivationState(actorId));
    }

    [Fact]
    public async Task Creating_entry_is_recoverable_while_directory_acquisition_is_in_flight()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var directory = new BlockingAcquireActorDirectory();
        await using var provider = CreateProvider(directory: directory);
        var catalog = provider.GetRequiredService<ActorActivationCatalog>();
        var source = provider.GetRequiredService<IActorActivationSnapshotSource>();
        var actorId = ActorId.From("catalog/creating-directory-claim");

        var creation = catalog.CreateAsync<HostedTestActor>(actorId, cancellationToken).AsTask();
        await directory.AcquireEntered.Task.WaitAsync(cancellationToken);

        try
        {
            Assert.Equal(ActorActivationState.Creating, catalog.GetActivationState(actorId));
            var claim = Assert.Single(source.CaptureRecoveryClaims(), record => record.ActorId == actorId);
            Assert.Equal(directory.ProposedActivation, claim.ActivationId);
            await Assert.ThrowsAsync<ActorNotFoundException>(async () =>
                await catalog.AskAsync<HostedTestActor, int>(
                    actorId,
                    static (actor, _) => actor.GetActivatedCountAsync(),
                    cancellationToken));
        }
        finally
        {
            directory.AllowAcquire.TrySetResult();
        }

        await creation;

        Assert.Equal(ActorActivationState.Valid, catalog.GetActivationState(actorId));
    }

    [Fact]
    public async Task CreateAsync_rolls_back_actor_and_route_when_actor_start_hook_fails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var dispatcher = new ThrowingActorLifecycleDispatcher(throwOnStart: true);
        await using var provider = CreateProvider(lifecycleDispatcher: dispatcher);
        var hosting = provider.GetRequiredService<ActorActivationCatalog>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var directory = provider.GetRequiredService<IActorDirectory>();
        var cache = provider.GetRequiredService<IActorDirectoryCache>();
        var actorId = ActorId.From("hosting/start-hook-fails");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await hosting.CreateAsync<HostedTestActor>(actorId, cancellationToken));

        Assert.Null(await directory.ResolveAsync(actorId, cancellationToken));
        Assert.False(cache.TryGet(actorId, out _));
        Assert.DoesNotContain(actorId, runtime.GetActiveActorIds(typeof(HostedTestActor)));
    }

    [Fact]
    public async Task CreateAsync_skips_directory_and_cache_for_local_only_actor()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider();
        var hosting = provider.GetRequiredService<ActorActivationCatalog>();
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
        var hosting = provider.GetRequiredService<ActorActivationCatalog>();
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
        var hosting = provider.GetRequiredService<ActorActivationCatalog>();
        var directory = provider.GetRequiredService<IActorDirectory>();
        var cache = provider.GetRequiredService<IActorDirectoryCache>();
        var actorId = ActorId.From("hosting/remote-owned");
        await directory.AcquireAsync(actorId, RemoteReference, ActorActivationId.New(), cancellationToken);

        await Assert.ThrowsAsync<ActorHostedElsewhereException>(async () =>
            await hosting.CreateAsync<HostedTestActor>(actorId, cancellationToken));

        var record = await directory.ResolveAsync(actorId, cancellationToken);
        Assert.NotNull(record);
        Assert.Equal(RemoteNode, record.Node);
        Assert.False(cache.TryGet(actorId, out _));
    }

    [Fact]
    public async Task CreateAsync_fails_when_directory_already_has_a_local_activation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider();
        var catalog = provider.GetRequiredService<ActorActivationCatalog>();
        var directory = provider.GetRequiredService<IActorDirectory>();
        var actorId = ActorId.From("catalog/local-claim-exists");
        var current = await directory.AcquireAsync(
            actorId,
            LocalReference,
            ActorActivationId.New(),
            cancellationToken);

        await Assert.ThrowsAsync<ActorAlreadyHostedException>(async () =>
            await catalog.CreateAsync<HostedTestActor>(actorId, cancellationToken));

        Assert.Equal(
            current.Record.ActivationId,
            (await directory.ResolveAsync(actorId, cancellationToken))!.ActivationId);
        Assert.Equal(ActorActivationState.Invalid, catalog.GetActivationState(actorId));
    }

    [Fact]
    public async Task CreateAsync_rolls_back_directory_cache_and_local_actor_when_local_create_fails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider();
        var hosting = provider.GetRequiredService<ActorActivationCatalog>();
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
    public async Task CreateAsync_reports_unconfirmed_compensation_when_directory_resolve_stalls()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var directory = new StallingRollbackDirectory();
        var time = new ManualDeadlineTimeProvider();
        var lifetime = new ActorCompensationLifetime(TimeSpan.FromSeconds(30), time);
        await using var provider = CreateProvider(directory: directory, compensationLifetime: lifetime);
        var hosting = provider.GetRequiredService<ActorActivationCatalog>();
        var catalog = provider.GetRequiredService<IActorActivationSnapshotSource>();
        var actorId = ActorId.From("hosting/stalled-rollback");

        var create = hosting.CreateAsync<FailingActivationActor>(actorId, cancellationToken).AsTask();
        await Task.WhenAll(directory.RollbackResolveStarted, time.TimerScheduled);
        time.Expire();

        var exception = await Assert.ThrowsAsync<ActorHostingException>(async () => await create);
        Assert.Contains("compensation", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unconfirmed", exception.Message, StringComparison.OrdinalIgnoreCase);
        var failures = Assert.IsType<AggregateException>(exception.InnerException).InnerExceptions;
        Assert.Contains(failures, failure => failure is ActorCompensationTimeoutException);
        Assert.Contains(catalog.CaptureRecoveryClaims(), record => record.ActorId == actorId);
    }

    [Fact]
    public async Task CreateAsync_does_not_open_admission_when_registered_claim_disappears_before_revalidation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var directory = new VanishingActivationDirectory();
        await using var provider = CreateProvider(directory: directory);
        var hosting = provider.GetRequiredService<ActorActivationCatalog>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var actorId = ActorId.From("hosting/claim-vanished");

        await Assert.ThrowsAsync<ActorDirectoryUnavailableException>(async () =>
            await hosting.CreateAsync<HostedTestActor>(actorId, cancellationToken));

        Assert.DoesNotContain(actorId, runtime.GetActiveActorIds(typeof(HostedTestActor)));
        Assert.DoesNotContain(
            provider.GetRequiredService<IActorActivationSnapshotSource>().CaptureRecoveryClaims(),
            record => record.ActorId == actorId);
    }

    [Fact]
    public async Task EnsureAsync_returns_when_same_type_actor_is_already_active_locally()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider();
        var hosting = provider.GetRequiredService<ActorActivationCatalog>();
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
        var hosting = provider.GetRequiredService<ActorActivationCatalog>();
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
        var hosting = provider.GetRequiredService<ActorActivationCatalog>();
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
        var hosting = provider.GetRequiredService<ActorActivationCatalog>();
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
        var hosting = provider.GetRequiredService<ActorActivationCatalog>();
        var directory = provider.GetRequiredService<IActorDirectory>();
        var cache = provider.GetRequiredService<IActorDirectoryCache>();
        var actorId = ActorId.From("hosting/stale-cache");

        await directory.AcquireAsync(actorId, RemoteReference, ActorActivationId.New(), cancellationToken);
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
        var hosting = provider.GetRequiredService<ActorActivationCatalog>();
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
    public async Task DestroyAsync_keeps_recovery_evidence_until_exact_release_succeeds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var directory = new FailingReleaseActivationDirectory();
        await using var provider = CreateProvider(directory: directory);
        var hosting = provider.GetRequiredService<ActorActivationCatalog>();
        var catalog = provider.GetRequiredService<IActorActivationSnapshotSource>();
        var actorId = ActorId.From("hosting/release-fails");

        await hosting.CreateAsync<HostedTestActor>(actorId, cancellationToken);

        await Assert.ThrowsAsync<ActorDirectoryUnavailableException>(async () =>
            await hosting.DestroyAsync<HostedTestActor>(actorId, cancellationToken));

        var record = Assert.Single(
            catalog.CaptureRecoveryClaims(),
            record => record.ActorId == actorId);
        Assert.Equal(actorId, record.ActorId);
        Assert.Equal(directory.Record!.ActivationId, record.ActivationId);
    }

    [Fact]
    public async Task DestroyExactAsync_does_not_destroy_a_replacement_activation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider();
        var hosting = provider.GetRequiredService<ActorActivationCatalog>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var readableDirectory = provider.GetRequiredService<IActorDirectory>();
        var directory = readableDirectory;
        var actorId = ActorId.From("hosting/stale-destroy");
        var owner = LocalReference;
        var currentActivation = new ActorActivationId(Guid.Parse("83000000-0000-0000-0000-000000000000"));
        var acquired = await directory.AcquireAsync(actorId, owner, currentActivation, cancellationToken);
        await hosting.ActivateExactAsync(
            typeof(HostedTestActor),
            new ActorLifecycleTarget(actorId, owner, currentActivation),
            ActorPlacementCreateMode.Ensure,
            cancellationToken);

        await hosting.DestroyExactAsync<HostedTestActor>(
            actorId,
            owner,
            new ActorActivationId(Guid.Parse("84000000-0000-0000-0000-000000000000")),
            cancellationToken);

        Assert.Equal(ActorState.Active, runtime.GetState(actorId));
        Assert.Equal(currentActivation, (await readableDirectory.ResolveAsync(actorId, cancellationToken))!.ActivationId);

        await hosting.DestroyExactAsync<HostedTestActor>(
            actorId,
            new NodeReference(
                owner.Cluster,
                owner.Node,
                new NodeIncarnationId(Guid.Parse("85000000-0000-0000-0000-000000000000"))),
            currentActivation,
            cancellationToken);

        Assert.Equal(ActorState.Active, runtime.GetState(actorId));

        await hosting.DestroyExactAsync<HostedTestActor>(
            actorId,
            owner,
            currentActivation,
            cancellationToken);
    }

    [Fact]
    public async Task Exact_call_does_not_dispatch_to_a_replacement_activation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider();
        var hosting = provider.GetRequiredService<ActorActivationCatalog>();
        var catalog = provider.GetRequiredService<ActorActivationCatalog>();
        var directory = provider.GetRequiredService<IActorDirectory>();
        var actorId = ActorId.From("hosting/stale-call");

        await hosting.CreateAsync<HostedTestActor>(actorId, cancellationToken);
        var first = Assert.IsType<ActorDirectoryRecord>(
            await directory.ResolveAsync(actorId, cancellationToken));

        await hosting.DestroyAsync<HostedTestActor>(actorId, cancellationToken);
        await hosting.CreateAsync<HostedTestActor>(actorId, cancellationToken);
        var replacement = Assert.IsType<ActorDirectoryRecord>(
            await directory.ResolveAsync(actorId, cancellationToken));
        Assert.NotEqual(first.ActivationId, replacement.ActivationId);

        var exception = await Assert.ThrowsAsync<ActorNotFoundException>(async () =>
            await ((IActorActivationDispatcher)catalog).AskExactAsync(
                typeof(HostedTestActor),
                actorId,
                first.ActivationId,
                static async (actor, _) => await ((HostedTestActor)actor).GetActivatedCountAsync(),
                cancellationToken));

        Assert.True(exception.DefinitelyNotExecuted);
    }

    [Fact]
    public async Task DestroyAsync_runs_actor_stop_hook()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var dispatcher = new RecordingActorLifecycleDispatcher();
        await using var provider = CreateProvider(lifecycleDispatcher: dispatcher);
        var hosting = provider.GetRequiredService<ActorActivationCatalog>();
        var actorId = ActorId.From("hosting/stop-hook");

        await hosting.CreateAsync<HostedTestActor>(actorId, cancellationToken);
        await hosting.DestroyAsync<HostedTestActor>(actorId, cancellationToken);

        Assert.Equal(
            [
                ("start", actorId.Value, typeof(HostedTestActor)),
                ("stop", actorId.Value, typeof(HostedTestActor))
            ],
            dispatcher.Events);
    }

    [Fact]
    public async Task ActorHosting_uses_hotfix_actor_lifecycle_dispatcher_when_hotfix_runtime_is_loaded()
    {
        HotfixLifecycleHostedFixture.Events.Clear();
        var cancellationToken = TestContext.Current.CancellationToken;
        var scan = HotfixBehaviorScanner.Scan(
            typeof(HotfixLifecycleHostedFixture.RoomBehavior).Assembly,
            [typeof(HotfixLifecycleHostedFixture.RoomBehavior)]);
        Assert.True(scan.Succeeded, string.Join(Environment.NewLine, scan.Diagnostics));
        await using var table = new HotfixDispatchTable(
            1,
            scan.Methods,
            scan.Services,
            scan.ActorMethods,
            scan.ActorLifecycles);
        await using var hotfixServices = new ServiceCollection()
            .AddSingleton(new HotfixLifecycleHostedFixture.Marker("hotfix"))
            .BuildServiceProvider();
        table.ValidateModuleActivation(hotfixServices);
        var snapshot = new HotfixRuntimeSnapshot(
            new HotfixServiceInvoker(table),
            hotfixServices,
            table,
            hotfixServices,
            typeof(HotfixLifecycleHostedFixture.RoomBehavior).Assembly,
            loadContext: null,
            sourceVersion: "test",
            sourcePath: null,
            ownsRuntimeResources: false,
            onRetired: null);
        var rootServices = new ServiceCollection()
            .AddSingleton<IHotfixRuntimeAccessor>(new FixedHotfixRuntimeAccessor(snapshot))
            .AddSingleton<HotfixActorLifecycleInvoker>()
            .BuildServiceProvider();
        var dispatcher = new HotfixActorLifecycleDispatcher(rootServices);
        await using var provider = CreateProvider(lifecycleDispatcher: dispatcher);
        var hosting = provider.GetRequiredService<ActorActivationCatalog>();
        var actorId = ActorId.From("hosting/hotfix-lifecycle");

        await hosting.CreateAsync<HotfixLifecycleHostedFixture.RoomActor>(actorId, cancellationToken);
        await hosting.DestroyAsync<HotfixLifecycleHostedFixture.RoomActor>(actorId, cancellationToken);

        Assert.Equal(["start:hosting/hotfix-lifecycle:hotfix", "stop:hosting/hotfix-lifecycle:hotfix"], HotfixLifecycleHostedFixture.Events);
    }

    [Fact]
    public async Task ActorHosting_allows_hotfix_actor_start_hook_to_create_timer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var timerBackend = new LifecycleTimerBackend();
        var scan = HotfixBehaviorScanner.Scan(
            typeof(HotfixLifecycleTimerFixture.RoomBehavior).Assembly,
            [typeof(HotfixLifecycleTimerFixture.RoomBehavior)]);
        Assert.True(scan.Succeeded, string.Join(Environment.NewLine, scan.Diagnostics));
        await using var table = new HotfixDispatchTable(
            1,
            scan.Methods,
            scan.Services,
            scan.ActorMethods,
            scan.ActorLifecycles);
        await using var hotfixServices = new ServiceCollection()
            .AddSingleton<ILakonaTimerBackend>(timerBackend)
            .BuildServiceProvider();
        table.ValidateModuleActivation(hotfixServices);
        var snapshot = new HotfixRuntimeSnapshot(
            new HotfixServiceInvoker(table),
            hotfixServices,
            table,
            hotfixServices,
            typeof(HotfixLifecycleTimerFixture.RoomBehavior).Assembly,
            loadContext: null,
            sourceVersion: "test",
            sourcePath: null,
            ownsRuntimeResources: false,
            onRetired: null);
        var rootServices = new ServiceCollection()
            .AddSingleton<IHotfixRuntimeAccessor>(new FixedHotfixRuntimeAccessor(snapshot))
            .AddSingleton<HotfixActorLifecycleInvoker>()
            .BuildServiceProvider();
        var dispatcher = new HotfixActorLifecycleDispatcher(rootServices);
        await using var provider = CreateProvider(lifecycleDispatcher: dispatcher);

        await provider.GetRequiredService<ActorActivationCatalog>()
            .CreateAsync<HotfixLifecycleTimerFixture.RoomActor>(ActorId.From("hosting/hotfix-lifecycle-timer"), cancellationToken);

        Assert.Equal(1, timerBackend.PeriodicTimerCount);
    }

    [Fact]
    public async Task DestroyAsync_logs_actor_stop_hook_failure_and_completes_cleanup()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var dispatcher = new ThrowingActorLifecycleDispatcher(throwOnStop: true);
        await using var provider = CreateProvider(lifecycleDispatcher: dispatcher);
        var hosting = provider.GetRequiredService<ActorActivationCatalog>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var directory = provider.GetRequiredService<IActorDirectory>();
        var cache = provider.GetRequiredService<IActorDirectoryCache>();
        var actorId = ActorId.From("hosting/stop-hook-fails");

        await hosting.CreateAsync<HostedTestActor>(actorId, cancellationToken);

        await hosting.DestroyAsync<HostedTestActor>(actorId, cancellationToken);

        Assert.Null(await directory.ResolveAsync(actorId, cancellationToken));
        Assert.False(cache.TryGet(actorId, out _));
        Assert.DoesNotContain(actorId, runtime.GetActiveActorIds(typeof(HostedTestActor)));
        Assert.Equal(ActorState.Dead, runtime.GetState(actorId));
    }

    [Fact]
    public async Task DestroyAsync_is_idempotent_when_actor_and_route_are_absent()
    {
        await using var provider = CreateProvider();
        var hosting = provider.GetRequiredService<ActorActivationCatalog>();

        await hosting.DestroyAsync<HostedTestActor>(
            ActorId.From("hosting/missing"),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DestroyAsync_preserves_remote_route_but_removes_stale_local_cache_and_cell()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider();
        var hosting = provider.GetRequiredService<ActorActivationCatalog>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var directory = provider.GetRequiredService<IActorDirectory>();
        var cache = provider.GetRequiredService<IActorDirectoryCache>();
        var actorId = ActorId.From("hosting/remote-preserved");

        await hosting.CreateAsync<HostedTestActor>(actorId, cancellationToken);
        var localRecord = await directory.ResolveAsync(actorId, cancellationToken);
        Assert.NotNull(localRecord);
        await directory.ReleaseAsync(actorId, localRecord.ActivationId, cancellationToken);
        await directory.AcquireAsync(actorId, RemoteReference, ActorActivationId.New(), cancellationToken);
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
        await using var provider = CreateProvider();
        var hosting = provider.GetRequiredService<ActorActivationCatalog>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var options = provider.GetRequiredService<ActorRuntimeOptions>();
        var directory = provider.GetRequiredService<IActorDirectory>();
        var cache = provider.GetRequiredService<IActorDirectoryCache>();
        var actorId = ActorId.From("hosting/stop-timeout");

        BlockingDeactivateActor.Reset();
        await hosting.CreateAsync<BlockingDeactivateActor>(actorId, cancellationToken);
        options.DeactivationTimeout = TimeSpan.FromMilliseconds(20);
        await Assert.ThrowsAsync<ActorHostingStopException>(async () =>
            await hosting.DestroyAsync<BlockingDeactivateActor>(actorId, cancellationToken));

        Assert.NotNull(await directory.ResolveAsync(actorId, cancellationToken));
        Assert.Equal(ActorState.Draining, runtime.GetState(actorId));
        BlockingDeactivateActor.ReleaseAll();
        await hosting.DestroyAsync<BlockingDeactivateActor>(actorId, cancellationToken);
    }

    [Fact]
    public async Task DestroyAsync_does_not_restore_local_cache_when_remote_owner_appears_after_stop_timeout()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var directory = new RemoteOwnerAfterLocalUnregisterDirectory();
        await using var provider = CreateProvider(directory: directory);
        var hosting = provider.GetRequiredService<ActorActivationCatalog>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var options = provider.GetRequiredService<ActorRuntimeOptions>();
        var cache = provider.GetRequiredService<IActorDirectoryCache>();
        var actorId = ActorId.From("hosting/timeout-remote-steals");

        BlockingDeactivateActor.Reset();
        await hosting.CreateAsync<BlockingDeactivateActor>(actorId, cancellationToken);
        options.DeactivationTimeout = TimeSpan.FromMilliseconds(20);
        await Assert.ThrowsAsync<ActorHostingStopException>(async () =>
            await hosting.DestroyAsync<BlockingDeactivateActor>(actorId, cancellationToken));

        var record = await directory.ResolveAsync(actorId, cancellationToken);
        Assert.NotNull(record);
        Assert.Equal(LocalNode, record.Node);
        Assert.Equal(ActorState.Draining, runtime.GetState(actorId));
        BlockingDeactivateActor.ReleaseAll();
        await hosting.DestroyAsync<BlockingDeactivateActor>(actorId, cancellationToken);
    }

    [Fact]
    public async Task DestroyAsync_logs_actor_deactivation_failure_and_completes_cleanup()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateProvider();
        var hosting = provider.GetRequiredService<ActorActivationCatalog>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var directory = provider.GetRequiredService<IActorDirectory>();
        var cache = provider.GetRequiredService<IActorDirectoryCache>();
        var actorId = ActorId.From("hosting/deactivate-throws");

        await hosting.CreateAsync<ThrowingDeactivateActor>(actorId, cancellationToken);

        await hosting.DestroyAsync<ThrowingDeactivateActor>(actorId, cancellationToken);

        Assert.Null(await directory.ResolveAsync(actorId, cancellationToken));
        Assert.DoesNotContain(actorId, runtime.GetActiveActorIds(typeof(ThrowingDeactivateActor)));
        Assert.Equal(ActorState.Dead, runtime.GetState(actorId));
    }

    [Fact]
    public async Task DestroyAsync_releases_only_its_exact_claim_when_stop_hook_throws()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var directory = new RemoteOwnerAfterLocalUnregisterDirectory();
        await using var provider = CreateProvider(directory: directory);
        var hosting = provider.GetRequiredService<ActorActivationCatalog>();
        var cache = provider.GetRequiredService<IActorDirectoryCache>();
        var actorId = ActorId.From("hosting/throw-remote-steals");

        await hosting.CreateAsync<ThrowingDeactivateActor>(actorId, cancellationToken);

        await hosting.DestroyAsync<ThrowingDeactivateActor>(actorId, cancellationToken);

        var record = await directory.ResolveAsync(actorId, cancellationToken);
        Assert.NotNull(record);
        Assert.Equal(RemoteNode, record.Node);
    }

    [Fact]
    public async Task CreateEnsureDestroy_for_same_actor_id_are_serialized()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var directory = new DelayingActorDirectory();
        await using var provider = CreateProvider(directory: directory);
        var hosting = provider.GetRequiredService<ActorActivationCatalog>();
        var actorId = ActorId.From("hosting/serialized");

        var create = hosting.CreateAsync<HostedTestActor>(actorId, cancellationToken).AsTask();
        var destroy = hosting.DestroyAsync<HostedTestActor>(actorId, cancellationToken).AsTask();

        await Task.WhenAll(create, destroy);

        Assert.Equal(1, directory.MaxConcurrentOperations);
    }

    [Fact]
    public async Task Graceful_lifecycle_drain_retires_every_activation_and_releases_its_exact_routes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var directory = new TestActorDirectory();
        var dispatcher = new RecordingActorLifecycleDispatcher();
        await using var provider = CreateProvider(directory: directory, lifecycleDispatcher: dispatcher);
        var placement = provider.GetRequiredService<ActorActivationCatalog>();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var lifecycle = provider.GetRequiredService<IActorActivationLifecycle>();
        var first = ActorId.From("shutdown/first");
        var second = ActorId.From("shutdown/second");

        await placement.CreateAsync<HostedTestActor>(first, cancellationToken);
        await placement.CreateAsync<HostedTestActor>(second, cancellationToken);

        await lifecycle.DrainAsync(cancellationToken);

        Assert.Equal(ActorState.Dead, runtime.GetState(first));
        Assert.Equal(ActorState.Dead, runtime.GetState(second));
        Assert.Null(await directory.ResolveAsync(first, cancellationToken));
        Assert.Null(await directory.ResolveAsync(second, cancellationToken));
        Assert.Equal(
            [first.Value, second.Value],
            dispatcher.Events
                .Where(static entry => entry.Kind == "stop")
                .Select(static entry => entry.ActorId)
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Graceful_lifecycle_drain_rejects_new_activations()
    {
        await using var provider = CreateProvider();
        var placement = provider.GetRequiredService<ActorActivationCatalog>();
        var lifecycle = provider.GetRequiredService<IActorActivationLifecycle>();

        await lifecycle.DrainAsync(TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await placement.CreateAsync<HostedTestActor>(
                ActorId.From("shutdown/late"),
                TestContext.Current.CancellationToken));
        Assert.Contains("draining", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ServiceProvider CreateProvider(
        Action<ActorRuntimeOptions>? configure = null,
        IActorDirectory? directory = null,
        IActorLifecycleDispatcher? lifecycleDispatcher = null,
        ActorCompensationLifetime? compensationLifetime = null)
    {
        directory ??= new TestActorDirectory();
        var identity = new LocalActorNodeIdentity(LocalNode);
        identity.Observe(LocalReference);
        var services = new ServiceCollection()
            .AddSingleton(identity);
        if (compensationLifetime is not null)
            services.AddSingleton(compensationLifetime);

        services
            .AddLakonaGameServerActors(configure)
            .AddSingleton(directory)
            .AddSingleton<IActorDirectoryCache, InMemoryActorDirectoryCache>();

        if (lifecycleDispatcher is not null)
        {
            services.AddSingleton(lifecycleDispatcher);
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
        }

        public static void Reset() => _release = NewRelease();

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

    private sealed class RecordingActorLifecycleDispatcher : IActorLifecycleDispatcher
    {
        public List<(string Kind, string ActorId, Type ActorType)> Events { get; } = [];

        public bool HasStartHook(Type actorType)
        {
            return true;
        }

        public bool HasStopHook(Type actorType)
        {
            return true;
        }

        public ValueTask StartAsync(
            Type actorType,
            ActorId actorId,
            object actor,
            CancellationToken cancellationToken)
        {
            Assert.IsType(actorType, actor);
            Events.Add(("start", actorId.Value, actorType));
            return default;
        }

        public ValueTask StopAsync(
            Type actorType,
            ActorId actorId,
            object actor,
            CancellationToken cancellationToken)
        {
            Assert.IsType(actorType, actor);
            Events.Add(("stop", actorId.Value, actorType));
            return default;
        }
    }

    private sealed class BlockingStartActorLifecycleDispatcher : IActorLifecycleDispatcher
    {
        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool HasStartHook(Type actorType) => true;

        public bool HasStopHook(Type actorType) => false;

        public async ValueTask StartAsync(
            Type actorType,
            ActorId actorId,
            object actor,
            CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }

        public ValueTask StopAsync(
            Type actorType,
            ActorId actorId,
            object actor,
            CancellationToken cancellationToken = default) => default;
    }

    private sealed class ThrowingActorLifecycleDispatcher(
        bool throwOnStart = false,
        bool throwOnStop = false) : IActorLifecycleDispatcher
    {
        public bool HasStartHook(Type actorType)
        {
            return true;
        }

        public bool HasStopHook(Type actorType)
        {
            return true;
        }

        public ValueTask StartAsync(
            Type actorType,
            ActorId actorId,
            object actor,
            CancellationToken cancellationToken)
        {
            if (throwOnStart)
            {
                throw new InvalidOperationException("start failed");
            }

            return default;
        }

        public ValueTask StopAsync(
            Type actorType,
            ActorId actorId,
            object actor,
            CancellationToken cancellationToken)
        {
            if (throwOnStop)
            {
                throw new InvalidOperationException("stop failed");
            }

            return default;
        }
    }

    public static class HotfixLifecycleHostedFixture
    {
        public static List<string> Events { get; } = [];

        public sealed class RoomActor : GameActor
        {
        }

        public sealed record Marker(string Value);

        [Lakona.Game.Server.Hotfix.Abstractions.HotfixBehaviorOf(typeof(RoomActor))]
        public sealed class RoomBehavior
        {
            [Lakona.Game.Server.Hotfix.Abstractions.ActorStart]
            public ValueTask StartAsync(RoomActor self, Lakona.Game.Server.Hotfix.Abstractions.ActorStartCall call)
            {
                _ = self;
                var marker = call.Services.GetRequiredService<Marker>();
                Events.Add($"start:{call.ActorId}:{marker.Value}");
                return default;
            }

            [Lakona.Game.Server.Hotfix.Abstractions.ActorStop]
            public ValueTask StopAsync(RoomActor self, Lakona.Game.Server.Hotfix.Abstractions.ActorStopCall call)
            {
                _ = self;
                var marker = call.Services.GetRequiredService<Marker>();
                Events.Add($"stop:{call.ActorId}:{marker.Value}");
                return default;
            }
        }
    }

    public static class HotfixLifecycleTimerFixture
    {
        public sealed class RoomActor : GameActor;

        public sealed class TimerArgs;

        public sealed class TimerCallback;

        public static HotfixTimerEntry<TimerArgs> TimerEntry { get; } = new(
            typeof(TimerCallback).FullName!,
            "TickAsync",
            42UL);

        [Lakona.Game.Server.Hotfix.Abstractions.HotfixBehaviorOf(typeof(RoomActor))]
        public sealed class RoomBehavior
        {
            [Lakona.Game.Server.Hotfix.Abstractions.ActorStart]
            public async ValueTask StartAsync(RoomActor self, Lakona.Game.Server.Hotfix.Abstractions.ActorStartCall call)
            {
                _ = self;
                await LakonaTimer.CreatePeriodicTimerAsync(
                    TimerEntry,
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(1),
                    new TimerArgs(),
                    call.CancellationToken);
            }
        }
    }

    private sealed class LifecycleTimerBackend : ILakonaTimerBackend
    {
        public int PeriodicTimerCount { get; private set; }

        public ValueTask<TimerId> CreateOnceTimerAsync<TArgs>(IHotfixTimerEntryResolver runtimeContext, HotfixTimerEntry<TArgs> callback, TimeSpan dueTime, TArgs args, CancellationToken cancellationToken) =>
            new(TimerId.FromGuid(Guid.NewGuid()));

        public ValueTask<TimerId> CreatePeriodicTimerAsync<TArgs>(IHotfixTimerEntryResolver runtimeContext, HotfixTimerEntry<TArgs> callback, TimeSpan dueTime, TimeSpan period, TArgs args, CancellationToken cancellationToken)
        {
            PeriodicTimerCount++;
            return new ValueTask<TimerId>(TimerId.FromGuid(Guid.NewGuid()));
        }

        public ValueTask DestroyTimerAsync(TimerId timerId, CancellationToken cancellationToken) => default;
    }

    private sealed class FixedHotfixRuntimeAccessor(HotfixRuntimeSnapshot snapshot) : IHotfixRuntimeAccessor
    {
        public HotfixRuntimeSnapshot Current => snapshot;
    }

    private sealed class DelayingActorDirectory : IActorDirectory
    {
        private readonly TestActorDirectory _inner = new();
        private int _currentOperations;

        public int MaxConcurrentOperations { get; private set; }

        public async ValueTask<ActorDirectoryRecord?> ResolveAsync(
            ActorId actorId,
            CancellationToken cancellationToken = default)
        {
            using var _ = await EnterAsync(cancellationToken);
            return await _inner.ResolveAsync(actorId, cancellationToken);
        }

        public async ValueTask<ActorActivationAcquireResult> AcquireAsync(
            ActorId actorId,
            NodeReference proposedOwner,
            ActorActivationId proposedActivation,
            CancellationToken cancellationToken = default)
        {
            using var _ = await EnterAsync(cancellationToken);
            return await _inner.AcquireAsync(actorId, proposedOwner, proposedActivation, cancellationToken);
        }

        public async ValueTask<bool> ReleaseAsync(
            ActorId actorId,
            ActorActivationId expectedActivation,
            CancellationToken cancellationToken = default)
        {
            using var _ = await EnterAsync(cancellationToken);
            return await _inner.ReleaseAsync(actorId, expectedActivation, cancellationToken);
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

    private sealed class BlockingAcquireActorDirectory : IActorDirectory
    {
        private readonly TestActorDirectory inner = new();

        public TaskCompletionSource AcquireEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowAcquire { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ActorActivationId ProposedActivation { get; private set; }

        public ValueTask<ActorDirectoryRecord?> ResolveAsync(
            ActorId actorId,
            CancellationToken cancellationToken = default) =>
            inner.ResolveAsync(actorId, cancellationToken);

        public async ValueTask<ActorActivationAcquireResult> AcquireAsync(
            ActorId actorId,
            NodeReference proposedOwner,
            ActorActivationId proposedActivation,
            CancellationToken cancellationToken = default)
        {
            ProposedActivation = proposedActivation;
            AcquireEntered.TrySetResult();
            await AllowAcquire.Task.WaitAsync(cancellationToken);
            return await inner.AcquireAsync(
                actorId,
                proposedOwner,
                proposedActivation,
                cancellationToken);
        }

        public ValueTask<bool> ReleaseAsync(
            ActorId actorId,
            ActorActivationId expectedActivation,
            CancellationToken cancellationToken = default) =>
            inner.ReleaseAsync(actorId, expectedActivation, cancellationToken);
    }

    private sealed class RemoteOwnerAfterLocalUnregisterDirectory : IActorDirectory
    {
        private readonly TestActorDirectory _inner = new();

        public ValueTask<ActorDirectoryRecord?> ResolveAsync(
            ActorId actorId,
            CancellationToken cancellationToken = default)
        {
            return _inner.ResolveAsync(actorId, cancellationToken);
        }

        public ValueTask<ActorActivationAcquireResult> AcquireAsync(
            ActorId actorId,
            NodeReference proposedOwner,
            ActorActivationId proposedActivation,
            CancellationToken cancellationToken = default)
        {
            return _inner.AcquireAsync(actorId, proposedOwner, proposedActivation, cancellationToken);
        }

        public async ValueTask<bool> ReleaseAsync(
            ActorId actorId,
            ActorActivationId expectedActivation,
            CancellationToken cancellationToken = default)
        {
            var released = await _inner.ReleaseAsync(actorId, expectedActivation, cancellationToken).ConfigureAwait(false);
            if (released)
            {
                await _inner.AcquireAsync(
                    actorId,
                    RemoteReference,
                    ActorActivationId.New(),
                    cancellationToken).ConfigureAwait(false);
            }

            return released;
        }
    }

    private sealed class VanishingActivationDirectory : IActorDirectory
    {
        private ActorDirectoryRecord? record;
        private int resolveCalls;

        public ValueTask<ActorDirectoryRecord?> ResolveAsync(
            ActorId actorId,
            CancellationToken cancellationToken = default) =>
            new(resolveCalls++ == 0 ? record : null);

        public ValueTask<ActorActivationAcquireResult> AcquireAsync(
            ActorId actorId,
            NodeReference proposedOwner,
            ActorActivationId proposedActivation,
            CancellationToken cancellationToken = default)
        {
            record ??= new ActorDirectoryRecord(actorId, proposedOwner, proposedActivation, DateTimeOffset.UtcNow);
            return new ValueTask<ActorActivationAcquireResult>(new ActorActivationAcquireResult(record, true));
        }

        public ValueTask<bool> ReleaseAsync(
            ActorId actorId,
            ActorActivationId expectedActivation,
            CancellationToken cancellationToken = default) => new(false);
    }

    private sealed class StallingRollbackDirectory : IActorDirectory
    {
        private readonly TaskCompletionSource<ActorDirectoryRecord?> stalledResolve =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private ActorDirectoryRecord? record;
        private int resolveCalls;

        public TaskCompletionSource RollbackResolveStartedSource { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task RollbackResolveStarted => RollbackResolveStartedSource.Task;

        public ValueTask<ActorDirectoryRecord?> ResolveAsync(
            ActorId actorId,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref resolveCalls) <= 2)
                return new ValueTask<ActorDirectoryRecord?>(record);

            RollbackResolveStartedSource.TrySetResult();
            return new ValueTask<ActorDirectoryRecord?>(stalledResolve.Task);
        }

        public ValueTask<ActorActivationAcquireResult> AcquireAsync(
            ActorId actorId,
            NodeReference proposedOwner,
            ActorActivationId proposedActivation,
            CancellationToken cancellationToken = default)
        {
            record ??= new ActorDirectoryRecord(actorId, proposedOwner, proposedActivation, DateTimeOffset.UtcNow);
            return new ValueTask<ActorActivationAcquireResult>(new ActorActivationAcquireResult(record, true));
        }

        public ValueTask<bool> ReleaseAsync(
            ActorId actorId,
            ActorActivationId expectedActivation,
            CancellationToken cancellationToken = default) => new(false);
    }

    private sealed class FailingReleaseActivationDirectory : IActorDirectory
    {
        public ActorDirectoryRecord? Record { get; private set; }

        public ValueTask<ActorDirectoryRecord?> ResolveAsync(
            ActorId actorId,
            CancellationToken cancellationToken = default) => new(Record);

        public ValueTask<ActorActivationAcquireResult> AcquireAsync(
            ActorId actorId,
            NodeReference proposedOwner,
            ActorActivationId proposedActivation,
            CancellationToken cancellationToken = default)
        {
            Record ??= new ActorDirectoryRecord(actorId, proposedOwner, proposedActivation, DateTimeOffset.UtcNow);
            return new ValueTask<ActorActivationAcquireResult>(new ActorActivationAcquireResult(Record, true));
        }

        public ValueTask<bool> ReleaseAsync(
            ActorId actorId,
            ActorActivationId expectedActivation,
            CancellationToken cancellationToken = default) =>
            throw new ActorDirectoryUnavailableException("Injected release failure.");
    }

}
