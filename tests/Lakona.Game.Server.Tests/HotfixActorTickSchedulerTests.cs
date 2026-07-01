using System.Reflection;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Hotfix.Loading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using GameActor = Lakona.Game.Server.Actors.Actor;

namespace Lakona.Game.Server.Tests;

[Collection(HotfixDispatchCollectionNames.GlobalState)]
public sealed class HotfixActorTickSchedulerTests : IDisposable
{
    public HotfixActorTickSchedulerTests()
    {
        TickHotfix.Reset();
    }

    public void Dispose()
    {
        HotfixDispatch.Replace(new HotfixDispatchTable(0, Array.Empty<HotfixMethodBinding>()));
        TickHotfix.Reset();
    }

    [Fact]
    public async Task Fixed_actor_tick_dispatches_through_current_hotfix_method()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var runtime = new RecordingActorRuntime();
        await using var scheduler = new HotfixActorTickScheduler(
            runtime,
            NullLogger<HotfixActorTickScheduler>.Instance);

        HotfixDispatch.Replace(CreateTickTable(1));
        scheduler.Apply(CreateSnapshot(
            new HotfixActorTickDeclaration(
                HotfixActorTickMode.FixedActor,
                typeof(TickActor),
                "fixed",
                nameof(TickHotfix.TickAsync),
                TimeSpan.FromMilliseconds(10),
                TickBacklogPolicy.SkipIfPending)));

        await TickHotfix.WaitForCountAsync(1, cancellationToken);

        HotfixDispatch.Replace(CreateTickTable(2));
        await TickHotfix.WaitForVersionAsync(2, cancellationToken);

        Assert.Equal(["fixed"], TickHotfix.ActorIds.Distinct().ToArray());
    }

    [Fact]
    public async Task Fixed_actor_tick_dispatch_enters_runtime_timer_scope()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var runtime = new RecordingActorRuntime();
        var backend = new RecordingTimerBackend();
        var table = CreateTimerTickTable(5);
        HotfixDispatch.Replace(new HotfixDispatchTable(0, Array.Empty<HotfixMethodBinding>()));
        await using var services = new ServiceCollection()
            .AddSingleton<ILakonaTimerBackend>(backend)
            .BuildServiceProvider();
        var accessor = new FixedRuntimeAccessor(new HotfixRuntimeSnapshot(
            new HotfixServiceInvoker(table),
            EmptyHotfixFeatureCommandInvoker.Instance,
            services,
            table,
            services,
            mainAssembly: null,
            loadContext: null,
            sourceVersion: null,
            sourceKind: null,
            sourcePath: null,
            ownsRuntimeResources: false,
            onRetired: null));
        await using var scheduler = new HotfixActorTickScheduler(
            runtime,
            NullLogger<HotfixActorTickScheduler>.Instance,
            accessor);

        scheduler.Apply(CreateSnapshot(
            new HotfixActorTickDeclaration(
                HotfixActorTickMode.FixedActor,
                typeof(TickActor),
                "fixed",
                nameof(TickHotfix.TickWithTimerAsync),
                TimeSpan.FromHours(1),
                TickBacklogPolicy.SkipIfPending)));

        await TickHotfix.WaitForVersionAsync(5, cancellationToken);

        Assert.Equal("fixed", backend.LastArgs?.Value);
    }

    [Fact]
    public async Task Fixed_actor_tick_dispatches_once_when_feature_is_applied()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var runtime = new RecordingActorRuntime();
        await using var scheduler = new HotfixActorTickScheduler(
            runtime,
            NullLogger<HotfixActorTickScheduler>.Instance);

        HotfixDispatch.Replace(CreateTickTable(1));
        scheduler.Apply(CreateSnapshot(
            new HotfixActorTickDeclaration(
                HotfixActorTickMode.FixedActor,
                typeof(TickActor),
                "fixed",
                nameof(TickHotfix.TickAsync),
                TimeSpan.FromHours(1),
                TickBacklogPolicy.SkipIfPending)));

        await TickHotfix.WaitForCountAsync(1, cancellationToken);

        Assert.Equal(["fixed"], TickHotfix.ActorIds.Distinct().ToArray());
    }

    [Fact]
    public async Task Fixed_actor_tick_does_not_create_missing_actor()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = new ServiceCollection()
            .AddLakonaGameServerActors()
            .BuildServiceProvider();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        await using var scheduler = new HotfixActorTickScheduler(
            runtime,
            NullLogger<HotfixActorTickScheduler>.Instance);

        HotfixDispatch.Replace(CreateTickTable(1));
        scheduler.Apply(CreateSnapshot(
            new HotfixActorTickDeclaration(
                HotfixActorTickMode.FixedActor,
                typeof(TickActor),
                "fixed",
                nameof(TickHotfix.TickAsync),
                TimeSpan.FromMilliseconds(10),
                TickBacklogPolicy.SkipIfPending)));

        await Task.Delay(60, cancellationToken);

        Assert.Empty(runtime.GetActiveActorIds(typeof(TickActor)));
        Assert.Empty(TickHotfix.ActorIds);
    }

    [Fact]
    public async Task Active_actor_tick_enumerates_active_actor_ids()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var runtime = new RecordingActorRuntime();
        runtime.ActiveActorIds[typeof(TickActor)] = [ActorId.From("a"), ActorId.From("b")];
        await using var scheduler = new HotfixActorTickScheduler(
            runtime,
            NullLogger<HotfixActorTickScheduler>.Instance);

        HotfixDispatch.Replace(CreateTickTable(1));
        scheduler.Apply(CreateSnapshot(
            new HotfixActorTickDeclaration(
                HotfixActorTickMode.ActiveActors,
                typeof(TickActor),
                "",
                nameof(TickHotfix.TickAsync),
                TimeSpan.FromMilliseconds(10),
                TickBacklogPolicy.SkipIfPending)));

        await TickHotfix.WaitForActorAsync("a", cancellationToken);
        await TickHotfix.WaitForActorAsync("b", cancellationToken);

        Assert.Contains("a", TickHotfix.ActorIds);
        Assert.Contains("b", TickHotfix.ActorIds);
    }

    [Fact]
    public async Task SkipIfPending_drops_ticks_while_actor_turn_is_pending()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var runtime = new RecordingActorRuntime();
        runtime.BlockedActorId = ActorId.From("fixed");
        await using var scheduler = new HotfixActorTickScheduler(
            runtime,
            NullLogger<HotfixActorTickScheduler>.Instance);

        HotfixDispatch.Replace(CreateTickTable(1));
        scheduler.Apply(CreateSnapshot(
            new HotfixActorTickDeclaration(
                HotfixActorTickMode.FixedActor,
                typeof(TickActor),
                "fixed",
                nameof(TickHotfix.TickAsync),
                TimeSpan.FromMilliseconds(10),
                TickBacklogPolicy.SkipIfPending)));

        await runtime.BlockedStarted.Task.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);
        await Task.Delay(60, cancellationToken);
        runtime.ReleaseBlocked();
        await TickHotfix.WaitForCountAsync(1, cancellationToken);
        await Task.Delay(30, cancellationToken);

        Assert.Equal(1, runtime.MaxQueuedWhileBlocked);
    }

    [Fact]
    public async Task Coalesce_keeps_one_follow_up_tick_while_actor_turn_is_pending()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var runtime = new RecordingActorRuntime();
        runtime.BlockedActorId = ActorId.From("fixed");
        await using var scheduler = new HotfixActorTickScheduler(
            runtime,
            NullLogger<HotfixActorTickScheduler>.Instance);

        HotfixDispatch.Replace(CreateTickTable(1));
        scheduler.Apply(CreateSnapshot(
            new HotfixActorTickDeclaration(
                HotfixActorTickMode.FixedActor,
                typeof(TickActor),
                "fixed",
                nameof(TickHotfix.TickAsync),
                TimeSpan.FromMilliseconds(10),
                TickBacklogPolicy.Coalesce)));

        await runtime.BlockedStarted.Task.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);
        await Task.Delay(60, cancellationToken);
        runtime.ReleaseBlocked();
        await TickHotfix.WaitForCountAsync(2, cancellationToken);

        Assert.Equal(1, runtime.MaxQueuedWhileBlocked);
    }

    [Fact]
    public async Task Observer_records_accepted_entered_skipped_and_coalesced_ticks()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var observer = new RecordingTickObserver();

        var skipRuntime = new RecordingActorRuntime { BlockedActorId = ActorId.From("skip") };
        await using (var skipScheduler = new HotfixActorTickScheduler(
            skipRuntime,
            NullLogger<HotfixActorTickScheduler>.Instance,
            observer))
        {
            HotfixDispatch.Replace(CreateTickTable(1));
            skipScheduler.Apply(CreateSnapshot(
                new HotfixActorTickDeclaration(
                    HotfixActorTickMode.FixedActor,
                    typeof(TickActor),
                    "skip",
                    nameof(TickHotfix.TickAsync),
                    TimeSpan.FromMilliseconds(10),
                    TickBacklogPolicy.SkipIfPending)));

            await skipRuntime.BlockedStarted.Task.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);
            await observer.WaitForSkippedAsync(cancellationToken);
            skipRuntime.ReleaseBlocked();
            await TickHotfix.WaitForCountAsync(1, cancellationToken);
        }

        TickHotfix.Reset();

        var coalesceRuntime = new RecordingActorRuntime { BlockedActorId = ActorId.From("coalesce") };
        await using (var coalesceScheduler = new HotfixActorTickScheduler(
            coalesceRuntime,
            NullLogger<HotfixActorTickScheduler>.Instance,
            observer))
        {
            HotfixDispatch.Replace(CreateTickTable(2));
            coalesceScheduler.Apply(CreateSnapshot(
                new HotfixActorTickDeclaration(
                    HotfixActorTickMode.FixedActor,
                    typeof(TickActor),
                    "coalesce",
                    nameof(TickHotfix.TickAsync),
                    TimeSpan.FromMilliseconds(10),
                    TickBacklogPolicy.Coalesce)));

            await coalesceRuntime.BlockedStarted.Task.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);
            await observer.WaitForCoalescedAsync(cancellationToken);
            coalesceRuntime.ReleaseBlocked();
            await TickHotfix.WaitForCountAsync(2, cancellationToken);
        }

        var accepted = observer.Accepted;
        var skipped = observer.Skipped;
        var coalesced = observer.Coalesced;
        var rejected = observer.Rejected;
        var entered = observer.Entered;

        Assert.Contains(accepted, observation => observation.ActorId == ActorId.From("skip"));
        Assert.Contains(accepted, observation => observation.ActorId == ActorId.From("coalesce"));
        Assert.Contains(skipped, observation => observation.ActorId == ActorId.From("skip"));
        Assert.Contains(coalesced, observation => observation.ActorId == ActorId.From("coalesce"));
        Assert.Empty(rejected);
        Assert.Contains(entered, observation => observation.ActorId == ActorId.From("skip") && observation.Sequence == 1);
        Assert.Contains(entered, observation => observation.ActorId == ActorId.From("coalesce") && observation.Sequence == 1);
        Assert.Contains(entered, observation => observation.ActorId == ActorId.From("coalesce") && observation.Sequence == 2);
        Assert.All(entered, observation => Assert.True(observation.QueueLatency >= TimeSpan.Zero));
    }

    [Fact]
    public async Task Observer_exceptions_do_not_prevent_fixed_actor_tick_execution()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var runtime = new RecordingActorRuntime();
        await using var scheduler = new HotfixActorTickScheduler(
            runtime,
            NullLogger<HotfixActorTickScheduler>.Instance,
            new ThrowingTickObserver());

        HotfixDispatch.Replace(CreateTickTable(1));
        scheduler.Apply(CreateSnapshot(
            new HotfixActorTickDeclaration(
                HotfixActorTickMode.FixedActor,
                typeof(TickActor),
                "fixed",
                nameof(TickHotfix.TickAsync),
                TimeSpan.FromHours(1),
                TickBacklogPolicy.SkipIfPending)));

        await TickHotfix.WaitForCountAsync(1, cancellationToken);

        Assert.Equal(["fixed"], TickHotfix.ActorIds.Distinct().ToArray());
    }

    [Fact]
    public async Task Hosted_service_does_not_dispatch_disabled_feature_ticks_on_start()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var runtime = new RecordingActorRuntime();
        await using var scheduler = new HotfixActorTickScheduler(
            runtime,
            NullLogger<HotfixActorTickScheduler>.Instance);
        var manager = new ReloadableHotfixManager(CreateSnapshot(
            "battle-runtime",
            CreateFixedTick("fixed", TimeSpan.FromMilliseconds(10))));
        var service = new HotfixActorTickHostedService(
            manager,
            scheduler,
            new LakonaGameRuntimeOptions { Feature = [] });

        HotfixDispatch.Replace(CreateTickTable(1));
        await service.StartAsync(cancellationToken);
        await Task.Delay(60, cancellationToken);
        await service.StopAsync(cancellationToken);

        Assert.Empty(TickHotfix.ActorIds);
    }

    [Fact]
    public async Task Hosted_service_dispatches_enabled_feature_ticks_on_start()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var runtime = new RecordingActorRuntime();
        await using var scheduler = new HotfixActorTickScheduler(
            runtime,
            NullLogger<HotfixActorTickScheduler>.Instance);
        var manager = new ReloadableHotfixManager(CreateSnapshot(
            "battle-runtime",
            CreateFixedTick("fixed", TimeSpan.FromMilliseconds(10))));
        var service = new HotfixActorTickHostedService(
            manager,
            scheduler,
            new LakonaGameRuntimeOptions { Feature = ["battle-runtime"] });

        HotfixDispatch.Replace(CreateTickTable(1));
        await service.StartAsync(cancellationToken);
        await TickHotfix.WaitForCountAsync(1, cancellationToken);
        await service.StopAsync(cancellationToken);

        Assert.Equal(["fixed"], TickHotfix.ActorIds.Distinct().ToArray());
    }

    [Fact]
    public async Task Hosted_service_dispatches_ticks_for_participant_created_local_actors()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = new ServiceCollection()
            .AddLakonaGameServerActors()
            .BuildServiceProvider();
        var runtime = provider.GetRequiredService<IActorRuntime>();
        var lifecycle = provider.GetRequiredService<IActorLifecycle>();
        await using var scheduler = new HotfixActorTickScheduler(
            runtime,
            NullLogger<HotfixActorTickScheduler>.Instance);
        var actorId = ActorId.From("fixed");
        var manager = new ReloadableHotfixManager(CreateSnapshot(
            "battle-runtime",
            [new HotfixLocalActorDeclaration(typeof(TickActor), actorId.Value)],
            [CreateFixedTick(actorId.Value, TimeSpan.FromMilliseconds(10))]));
        using var hotfixRuntime = CreateRuntime(manager.Current.Features, "1");
        var participant = new HotfixLocalActorPublicationParticipant(lifecycle);
        await participant.BeforePublishAsync(hotfixRuntime.Snapshot, cancellationToken);
        await participant.AfterPublishAsync(hotfixRuntime.Snapshot, hotfixRuntime.Snapshot, cancellationToken);
        var service = new HotfixActorTickHostedService(
            manager,
            scheduler,
            new LakonaGameRuntimeOptions { Feature = ["battle-runtime"] });

        HotfixDispatch.Replace(CreateTickTable(1));
        await service.StartAsync(cancellationToken);
        await TickHotfix.WaitForActorAsync(actorId.Value, cancellationToken);
        await service.StopAsync(cancellationToken);

        Assert.Contains(actorId, runtime.GetActiveActorIds(typeof(TickActor)));
        Assert.Contains(actorId.Value, TickHotfix.ActorIds);
    }

    [Fact]
    public void Production_feature_activation_policy_respects_configured_feature_order()
    {
        var policy = new HotfixFeatureActivationPolicy(new LakonaGameRuntimeOptions
        {
            Feature = ["beta", "alpha"]
        });
        var alpha = CreateFeature("alpha", [], []);
        var beta = CreateFeature("beta", [], []);
        var gamma = CreateFeature("gamma", [], []);

        var selected = policy.SelectActiveFeatures([alpha, beta, gamma]);

        Assert.Equal(["beta", "alpha"], selected.Select(static feature => feature.Name).ToArray());
    }

    [Fact]
    public async Task Local_actor_preflight_failure_keeps_old_publication_and_does_not_create_candidate_actors()
    {
        var actorLifecycle = new RecordingActorRuntime();
        var participant = new HotfixLocalActorPublicationParticipant(actorLifecycle);
        var manager = new HotfixManager(
            new UnusedAssemblySource(),
            participants: [participant]);
        using var oldRuntime = CreateRuntime([CreateFeature("old", [], [])], "1");
        using var candidateRuntime = CreateRuntime([
            CreateFeature(
                "candidate",
                [
                    new HotfixLocalActorDeclaration(typeof(TickActor), "created-before-failure"),
                    new HotfixLocalActorDeclaration(typeof(NotActor), "invalid")
                ],
                [])
        ], "2");
        var first = await manager.PublishCandidateAsync(
            oldRuntime.Snapshot,
            CreateSnapshot(1, oldRuntime.Snapshot.DispatchTable!.Features),
            oldRuntime.Snapshot.DispatchTable!.Features,
            TestContext.Current.CancellationToken);
        Assert.True(first.Succeeded, string.Join(Environment.NewLine, first.Diagnostics));
        var oldPublishedRuntime = ((IHotfixRuntimeAccessor)manager).Current;
        var oldPublishedSnapshot = manager.Current;
        var oldPublishedDispatch = HotfixDispatch.Current;

        var failed = await manager.PublishCandidateAsync(
            candidateRuntime.Snapshot,
            CreateSnapshot(2, candidateRuntime.Snapshot.DispatchTable!.Features),
            candidateRuntime.Snapshot.DispatchTable!.Features,
            TestContext.Current.CancellationToken);

        Assert.False(failed.Succeeded);
        Assert.Contains(failed.Diagnostics, diagnostic =>
            diagnostic.Contains(nameof(NotActor), StringComparison.Ordinal) &&
            diagnostic.Contains(nameof(IActor), StringComparison.Ordinal));
        Assert.Same(oldPublishedRuntime, ((IHotfixRuntimeAccessor)manager).Current);
        Assert.Same(oldPublishedSnapshot, manager.Current);
        Assert.Same(oldPublishedDispatch, HotfixDispatch.Current);
        Assert.Empty(actorLifecycle.GetActiveActorIds(typeof(TickActor)));
    }

    [Fact]
    public async Task Local_actor_create_failure_keeps_old_publication_and_reports_diagnostic()
    {
        await using var provider = new ServiceCollection()
            .AddLakonaGameServerActors()
            .BuildServiceProvider();
        var actorLifecycle = provider.GetRequiredService<IActorLifecycle>();
        var actorRuntime = provider.GetRequiredService<IActorRuntime>();
        var actorId = ActorId.From("candidate");
        var existing = await actorLifecycle.CreateLocalAsync(
            typeof(OtherActor),
            actorId,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(existing.Succeeded, existing.Diagnostic);
        var participant = new HotfixLocalActorPublicationParticipant(actorLifecycle);
        var manager = new HotfixManager(
            new UnusedAssemblySource(),
            participants: [participant]);
        using var oldRuntime = CreateRuntime([CreateFeature("old", [], [])], "1");
        using var candidateRuntime = CreateRuntime([
            CreateFeature(
                "candidate",
                [new HotfixLocalActorDeclaration(typeof(TickActor), actorId.Value)],
                [])
        ], "2");
        var first = await manager.PublishCandidateAsync(
            oldRuntime.Snapshot,
            CreateSnapshot(1, oldRuntime.Snapshot.DispatchTable!.Features),
            oldRuntime.Snapshot.DispatchTable!.Features,
            TestContext.Current.CancellationToken);
        Assert.True(first.Succeeded, string.Join(Environment.NewLine, first.Diagnostics));
        var oldPublishedRuntime = ((IHotfixRuntimeAccessor)manager).Current;
        var oldPublishedSnapshot = manager.Current;
        var oldPublishedDispatch = HotfixDispatch.Current;

        var failed = await manager.PublishCandidateAsync(
            candidateRuntime.Snapshot,
            CreateSnapshot(2, candidateRuntime.Snapshot.DispatchTable!.Features),
            candidateRuntime.Snapshot.DispatchTable!.Features,
            TestContext.Current.CancellationToken);

        Assert.False(failed.Succeeded);
        Assert.Contains(failed.Diagnostics, diagnostic =>
            diagnostic.Contains(actorId.Value, StringComparison.Ordinal) &&
            diagnostic.Contains(nameof(OtherActor), StringComparison.Ordinal));
        Assert.Same(oldPublishedRuntime, ((IHotfixRuntimeAccessor)manager).Current);
        Assert.Same(oldPublishedSnapshot, manager.Current);
        Assert.Same(oldPublishedDispatch, HotfixDispatch.Current);
        Assert.Empty(actorRuntime.GetActiveActorIds(typeof(TickActor)));
        Assert.Contains(actorId, actorRuntime.GetActiveActorIds(typeof(OtherActor)));
    }

    [Fact]
    public async Task Hosted_service_filters_successful_reload_feature_ticks()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var runtime = new RecordingActorRuntime();
        await using var scheduler = new HotfixActorTickScheduler(
            runtime,
            NullLogger<HotfixActorTickScheduler>.Instance);
        var manager = new ReloadableHotfixManager(CreateSnapshot(
            "disabled-runtime",
            CreateFixedTick("disabled", TimeSpan.FromMilliseconds(10))));
        var service = new HotfixActorTickHostedService(
            manager,
            scheduler,
            new LakonaGameRuntimeOptions { Feature = ["battle-runtime"] });

        HotfixDispatch.Replace(CreateTickTable(1));
        await service.StartAsync(cancellationToken);
        await Task.Delay(40, cancellationToken);
        manager.RaiseReload(CreateSnapshot(
            "battle-runtime",
            CreateFixedTick("enabled", TimeSpan.FromMilliseconds(10))));
        await TickHotfix.WaitForActorAsync("enabled", cancellationToken);
        await service.StopAsync(cancellationToken);

        Assert.DoesNotContain("disabled", TickHotfix.ActorIds);
        Assert.Contains("enabled", TickHotfix.ActorIds);
    }

    private static HotfixActorTickDeclaration CreateFixedTick(string actorId, TimeSpan interval)
    {
        return new HotfixActorTickDeclaration(
            HotfixActorTickMode.FixedActor,
            typeof(TickActor),
            actorId,
            nameof(TickHotfix.TickAsync),
            interval,
            TickBacklogPolicy.SkipIfPending);
    }

    private static HotfixSnapshot CreateSnapshot(params HotfixActorTickDeclaration[] ticks)
    {
        return CreateSnapshot("test", ticks);
    }

    private static HotfixSnapshot CreateSnapshot(string featureName, params HotfixActorTickDeclaration[] ticks)
    {
        return CreateSnapshot(featureName, [], ticks);
    }

    private static HotfixSnapshot CreateSnapshot(
        string featureName,
        IReadOnlyList<HotfixLocalActorDeclaration> localActors,
        IReadOnlyList<HotfixActorTickDeclaration> ticks)
    {
        var feature = CreateFeature(featureName, localActors, ticks);
        return new HotfixSnapshot(
            "test",
            "test.dll",
            null,
            DateTimeOffset.UtcNow,
            1,
            [],
            HotfixReloadStatus.Succeeded,
            null,
            null,
            [feature]);
    }

    private static HotfixSnapshot CreateSnapshot(
        long tableVersion,
        IReadOnlyList<HotfixFeatureDeclaration> features)
    {
        return new HotfixSnapshot(
            tableVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "test",
            $"test-{tableVersion}.dll",
            DateTimeOffset.UtcNow,
            tableVersion,
            [],
            HotfixReloadStatus.Succeeded,
            null,
            null,
            features);
    }

    private static HotfixFeatureDeclaration CreateFeature(
        string featureName,
        IReadOnlyList<HotfixLocalActorDeclaration> localActors,
        IReadOnlyList<HotfixActorTickDeclaration> ticks)
    {
        return new HotfixFeatureDeclaration(
            featureName,
            typeof(TestFeature),
            Discoverable: true,
            new Dictionary<string, string>(),
            localActors,
            ticks,
            [],
            []);
    }

    private static ScopedRuntime CreateRuntime(
        IReadOnlyList<HotfixFeatureDeclaration> features,
        string marker)
    {
        var tableVersion = long.Parse(marker, System.Globalization.CultureInfo.InvariantCulture);
        var table = new HotfixDispatchTable(
            tableVersion,
            Array.Empty<HotfixMethodBinding>(),
            Array.Empty<HotfixServiceMethodBinding>(),
            features);
        var provider = new ServiceCollection().BuildServiceProvider();
        var snapshot = new HotfixRuntimeSnapshot(
            new HotfixServiceInvoker(table),
            EmptyHotfixFeatureCommandInvoker.Instance,
            provider,
            table,
            provider,
            mainAssembly: typeof(HotfixActorTickSchedulerTests).Assembly,
            loadContext: null,
            sourceVersion: marker,
            sourceKind: null,
            sourcePath: null,
            ownsRuntimeResources: false,
            onRetired: null);
        return new ScopedRuntime(provider, snapshot);
    }

    private static HotfixDispatchTable CreateTickTable(long version)
    {
        var method = typeof(TickHotfix).GetMethod(
            nameof(TickHotfix.TickAsync),
            BindingFlags.Public | BindingFlags.Static)!;
        var binding = new HotfixMethodBinding(
            HotfixDispatch.CreateKey(
                typeof(TickActor),
                nameof(TickHotfix.TickAsync),
                typeof(ValueTask),
                [typeof(HotfixActorTick)]),
            method,
            typeof(TickActor),
            typeof(ValueTask),
            [typeof(HotfixActorTick)]);
        return new HotfixDispatchTable(version, [binding]);
    }

    private static HotfixDispatchTable CreateTimerTickTable(long version)
    {
        var method = typeof(TickHotfix).GetMethod(
            nameof(TickHotfix.TickWithTimerAsync),
            BindingFlags.Public | BindingFlags.Static)!;
        var binding = new HotfixMethodBinding(
            HotfixDispatch.CreateKey(
                typeof(TickActor),
                nameof(TickHotfix.TickWithTimerAsync),
                typeof(ValueTask),
                [typeof(HotfixActorTick)]),
            method,
            typeof(TickActor),
            typeof(ValueTask),
            [typeof(HotfixActorTick)]);
        return new HotfixDispatchTable(version, [binding]);
    }

    private sealed class FixedRuntimeAccessor(HotfixRuntimeSnapshot current) : IHotfixRuntimeAccessor
    {
        public HotfixRuntimeSnapshot Current { get; } = current;

        public HotfixRuntimeSnapshotLease AcquireCurrent()
        {
            return Current.AcquireLease();
        }
    }

    private sealed class UnusedAssemblySource : IHotfixAssemblySource
    {
        public ValueTask<HotfixAssemblySourceResult> ResolveAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class ScopedRuntime(ServiceProvider provider, HotfixRuntimeSnapshot snapshot) : IDisposable
    {
        public HotfixRuntimeSnapshot Snapshot { get; } = snapshot;

        public void Dispose()
        {
            provider.Dispose();
        }
    }

    private sealed class RecordingTimerBackend : ILakonaTimerBackend
    {
        public TimerArgs? LastArgs { get; private set; }

        public ValueTask<TimerId> CreateOnceTimerAsync<TCallback, TArgs>(
            TimeSpan dueTime,
            string methodName,
            TArgs args,
            CancellationToken cancellationToken)
            where TCallback : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (args is TimerArgs timerArgs)
            {
                LastArgs = timerArgs;
            }

            return new ValueTask<TimerId>(TimerId.FromGuid(Guid.NewGuid()));
        }

        public ValueTask<TimerId> CreatePeriodicTimerAsync<TCallback, TArgs>(
            TimeSpan dueTime,
            TimeSpan period,
            string methodName,
            TArgs args,
            CancellationToken cancellationToken)
            where TCallback : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (args is TimerArgs timerArgs)
            {
                LastArgs = timerArgs;
            }

            return new ValueTask<TimerId>(TimerId.FromGuid(Guid.NewGuid()));
        }

        public ValueTask DestroyTimerAsync(TimerId timerId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return default;
        }
    }

    private sealed record TimerArgs(string Value);

    private sealed class TimerCallbackTarget;

    private sealed class RecordingActorRuntime : IActorRuntime, IActorLifecycle
    {
        private readonly Dictionary<ActorId, TickActor> _actors = [];
        private readonly object _sync = new();
        private TaskCompletionSource? _blockedRelease;
        private int _queuedWhileBlocked;

        public Dictionary<Type, IReadOnlyList<ActorId>> ActiveActorIds { get; } = [];

        public string? CreateLocalFailureDiagnostic { get; set; }

        public ActorId? BlockedActorId { get; set; }

        public TaskCompletionSource BlockedStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int MaxQueuedWhileBlocked { get; private set; }

        public ValueTask<TActor> GetOrCreateAsync<TActor>(ActorId id, CancellationToken cancellationToken = default)
            where TActor : class, IActor
        {
            return new ValueTask<TActor>((TActor)(IActor)GetOrCreate(id));
        }

        public ValueTask<ActorCreateLocalResult> CreateLocalAsync<TActor>(
            ActorId actorId,
            ActorCreateOptions? options = null,
            CancellationToken cancellationToken = default)
            where TActor : class, IActor
        {
            return CreateLocalAsync(typeof(TActor), actorId, options, cancellationToken);
        }

        public ValueTask<ActorCreateLocalResult> CreateLocalAsync(
            Type actorType,
            ActorId actorId,
            ActorCreateOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _ = options;
            cancellationToken.ThrowIfCancellationRequested();
            if (CreateLocalFailureDiagnostic is not null)
            {
                return new ValueTask<ActorCreateLocalResult>(
                    new ActorCreateLocalResult(
                        ActorCreateLocalStatus.AlreadyExistsDifferentType,
                        actorId,
                        actorType,
                        CreateLocalFailureDiagnostic));
            }

            GetOrCreate(actorId);
            ActiveActorIds[actorType] = _actors.Keys.OrderBy(static id => id.Value).ToArray();
            return new ValueTask<ActorCreateLocalResult>(
                new ActorCreateLocalResult(ActorCreateLocalStatus.Created, actorId, actorType));
        }

        public ValueTask<ActorDestroyLocalResult> DestroyLocalAsync<TActor>(
            ActorId actorId,
            ActorDestroyOptions? options = null,
            CancellationToken cancellationToken = default)
            where TActor : class, IActor
        {
            _ = options;
            cancellationToken.ThrowIfCancellationRequested();
            _actors.Remove(actorId);
            ActiveActorIds[typeof(TActor)] = _actors.Keys.OrderBy(static id => id.Value).ToArray();
            return new ValueTask<ActorDestroyLocalResult>(
                new ActorDestroyLocalResult(ActorDestroyLocalStatus.Destroyed, actorId, typeof(TActor)));
        }

        public ValueTask TellAsync<TActor>(
            ActorId id,
            Func<TActor, CancellationToken, ValueTask> message,
            CancellationToken cancellationToken = default)
            where TActor : class, IActor
        {
            return TellAsync(typeof(TActor), id, (actor, ct) => message((TActor)actor, ct), cancellationToken);
        }

        public ActorTellResult TryTell<TActor>(
            ActorId id,
            Func<TActor, CancellationToken, ValueTask> message,
            CancellationToken cancellationToken = default)
            where TActor : class, IActor
        {
            return TryTell(typeof(TActor), id, (actor, ct) => message((TActor)actor, ct), cancellationToken);
        }

        public ValueTask TellAsync(
            Type actorType,
            ActorId id,
            Func<IActor, CancellationToken, ValueTask> message,
            CancellationToken cancellationToken = default)
        {
            var result = TryTell(actorType, id, message, cancellationToken);
            if (result != ActorTellResult.Accepted)
            {
                throw new InvalidOperationException($"Actor tell failed with {result}.");
            }

            return default;
        }

        public ActorTellResult TryTell(
            Type actorType,
            ActorId id,
            Func<IActor, CancellationToken, ValueTask> message,
            CancellationToken cancellationToken = default)
        {
            _ = actorType;
            var actor = GetOrCreate(id);
            if (BlockedActorId == id)
            {
                lock (_sync)
                {
                    if (_blockedRelease is not null)
                    {
                        _queuedWhileBlocked++;
                        MaxQueuedWhileBlocked = Math.Max(MaxQueuedWhileBlocked, _queuedWhileBlocked);
                        return ActorTellResult.MailboxFull;
                    }

                    _blockedRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    _queuedWhileBlocked = 1;
                    MaxQueuedWhileBlocked = Math.Max(MaxQueuedWhileBlocked, _queuedWhileBlocked);
                }
            }

            _ = InvokeAsync(actor, id, message, cancellationToken);
            return ActorTellResult.Accepted;
        }

        public ValueTask<TResult> AskAsync<TActor, TResult>(
            ActorId id,
            Func<TActor, CancellationToken, ValueTask<TResult>> message,
            CancellationToken cancellationToken = default)
            where TActor : class, IActor
        {
            return message((TActor)(IActor)GetOrCreate(id), cancellationToken);
        }

        public IReadOnlyList<ActorId> GetActiveActorIds(Type actorType)
        {
            return ActiveActorIds.TryGetValue(actorType, out var ids) ? ids : [];
        }

        public IAsyncDisposable RegisterTimer<TActor>(
            ActorId id,
            TimeSpan dueTime,
            TimeSpan? period,
            Func<TActor, CancellationToken, ValueTask> callback)
            where TActor : class, IActor
        {
            throw new NotSupportedException();
        }

        public bool TryGetMailboxMetrics(ActorId id, out ActorMailboxMetrics metrics)
        {
            metrics = default;
            return false;
        }

        public ActorState GetState(ActorId id)
        {
            return _actors.ContainsKey(id) ? ActorState.Active : ActorState.Dead;
        }

        public ValueTask StopAsync(ActorId id)
        {
            _actors.Remove(id);
            RemoveActiveActorId(id);
            return default;
        }

        public ValueTask<ActorStopOutcome> StopAsync(ActorId id, TimeSpan drainTimeout)
        {
            _actors.Remove(id);
            RemoveActiveActorId(id);
            return new ValueTask<ActorStopOutcome>(ActorStopOutcome.Drained);
        }

        public void ReleaseBlocked()
        {
            TaskCompletionSource? release;
            lock (_sync)
            {
                release = _blockedRelease;
                _blockedRelease = null;
                _queuedWhileBlocked = 0;
                BlockedActorId = null;
            }

            release?.SetResult();
        }

        private async Task InvokeAsync(
            TickActor actor,
            ActorId id,
            Func<IActor, CancellationToken, ValueTask> message,
            CancellationToken cancellationToken)
        {
            TaskCompletionSource? release;
            lock (_sync)
            {
                release = BlockedActorId == id ? _blockedRelease : null;
            }

            if (release is not null)
            {
                BlockedStarted.TrySetResult();
                await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            await message(actor, cancellationToken).ConfigureAwait(false);
        }

        private TickActor GetOrCreate(ActorId id)
        {
            if (!_actors.TryGetValue(id, out var actor))
            {
                actor = new TickActor(id.Value);
                _actors.Add(id, actor);
            }

            return actor;
        }

        private void RemoveActiveActorId(ActorId id)
        {
            foreach (var entry in ActiveActorIds.ToArray())
            {
                ActiveActorIds[entry.Key] = entry.Value
                    .Where(activeId => activeId != id)
                    .OrderBy(static activeId => activeId.Value)
                    .ToArray();
            }
        }
    }

    private sealed class RecordingTickObserver : IHotfixActorTickSchedulerObserver
    {
        private readonly object _sync = new();
        private readonly List<HotfixActorTickDispatchObservation> _accepted = [];
        private readonly List<HotfixActorTickDispatchObservation> _skipped = [];
        private readonly List<HotfixActorTickDispatchObservation> _coalesced = [];
        private readonly List<(HotfixActorTickDispatchObservation Observation, ActorTellResult Result)> _rejected = [];
        private readonly List<HotfixActorTickEntryObservation> _entered = [];
        private readonly TaskCompletionSource _skippedRecorded =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _coalescedRecorded =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<HotfixActorTickDispatchObservation> Accepted
        {
            get
            {
                lock (_sync)
                {
                    return _accepted.ToArray();
                }
            }
        }

        public IReadOnlyList<HotfixActorTickDispatchObservation> Skipped
        {
            get
            {
                lock (_sync)
                {
                    return _skipped.ToArray();
                }
            }
        }

        public IReadOnlyList<HotfixActorTickDispatchObservation> Coalesced
        {
            get
            {
                lock (_sync)
                {
                    return _coalesced.ToArray();
                }
            }
        }

        public IReadOnlyList<HotfixActorTickEntryObservation> Entered
        {
            get
            {
                lock (_sync)
                {
                    return _entered.ToArray();
                }
            }
        }

        public IReadOnlyList<(HotfixActorTickDispatchObservation Observation, ActorTellResult Result)> Rejected
        {
            get
            {
                lock (_sync)
                {
                    return _rejected.ToArray();
                }
            }
        }

        public void OnDispatchAccepted(HotfixActorTickDispatchObservation observation)
        {
            lock (_sync)
            {
                _accepted.Add(observation);
            }
        }

        public void OnDispatchRejected(
            HotfixActorTickDispatchObservation observation,
            ActorTellResult result)
        {
            lock (_sync)
            {
                _rejected.Add((observation, result));
            }
        }

        public void OnDispatchSkipped(HotfixActorTickDispatchObservation observation)
        {
            lock (_sync)
            {
                _skipped.Add(observation);
            }

            _skippedRecorded.TrySetResult();
        }

        public void OnDispatchCoalesced(HotfixActorTickDispatchObservation observation)
        {
            lock (_sync)
            {
                _coalesced.Add(observation);
            }

            _coalescedRecorded.TrySetResult();
        }

        public void OnTickEntered(HotfixActorTickEntryObservation observation)
        {
            lock (_sync)
            {
                _entered.Add(observation);
            }
        }

        public async Task WaitForSkippedAsync(CancellationToken cancellationToken)
        {
            await _skippedRecorded.Task.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task WaitForCoalescedAsync(CancellationToken cancellationToken)
        {
            await _coalescedRecorded.Task.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private sealed class ThrowingTickObserver : IHotfixActorTickSchedulerObserver
    {
        public void OnDispatchAccepted(HotfixActorTickDispatchObservation observation)
        {
            throw new InvalidOperationException("Observer dispatch accepted failure.");
        }

        public void OnDispatchRejected(
            HotfixActorTickDispatchObservation observation,
            ActorTellResult result)
        {
            throw new InvalidOperationException("Observer dispatch rejected failure.");
        }

        public void OnDispatchSkipped(HotfixActorTickDispatchObservation observation)
        {
            throw new InvalidOperationException("Observer dispatch skipped failure.");
        }

        public void OnDispatchCoalesced(HotfixActorTickDispatchObservation observation)
        {
            throw new InvalidOperationException("Observer dispatch coalesced failure.");
        }

        public void OnTickEntered(HotfixActorTickEntryObservation observation)
        {
            throw new InvalidOperationException("Observer tick entered failure.");
        }
    }

    public sealed class TickActor : GameActor
    {
        private readonly string _recordingRuntimeId;

        public TickActor()
        {
            _recordingRuntimeId = "";
        }

        public TickActor(string id)
        {
            _recordingRuntimeId = id;
        }

        public string Id =>
            string.Equals(Context.Id.Value, "__uninitialized__", StringComparison.Ordinal)
                ? _recordingRuntimeId
                : Context.Id.Value;
    }

    public sealed class OtherActor : GameActor;

    private sealed class NotActor;

    private sealed class TestFeature;

    private sealed class ReloadableHotfixManager(HotfixSnapshot current) : IHotfixManager
    {
        public event EventHandler<HotfixReloadResult>? Reloaded;

        public HotfixSnapshot Current { get; private set; } = current;

        public ValueTask<HotfixReloadResult> ValidateAsync(CancellationToken cancellationToken = default)
        {
            return new ValueTask<HotfixReloadResult>(CreateResult(Current));
        }

        public ValueTask<HotfixReloadResult> ValidateAsync(
            IHotfixAssemblySource source,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<HotfixReloadResult>(CreateResult(Current));
        }

        public ValueTask<HotfixReloadResult> ReloadAsync(CancellationToken cancellationToken = default)
        {
            return new ValueTask<HotfixReloadResult>(CreateResult(Current));
        }

        public void RaiseReload(HotfixSnapshot snapshot)
        {
            Current = snapshot;
            Reloaded?.Invoke(this, CreateResult(snapshot));
        }

        private static HotfixReloadResult CreateResult(HotfixSnapshot snapshot)
        {
            return new HotfixReloadResult(
                HotfixReloadStatus.Succeeded,
                snapshot,
                snapshot.SourceKind,
                snapshot.SourcePath,
                []);
        }
    }

    public static class TickHotfix
    {
        private static readonly object Sync = new();
        private static readonly List<string> ActorIdList = [];
        private static readonly List<long> VersionList = [];

        public static IReadOnlyList<string> ActorIds
        {
            get
            {
                lock (Sync)
                {
                    return ActorIdList.ToArray();
                }
            }
        }

        public static ValueTask TickAsync(TickActor actor, HotfixActorTick tick)
        {
            lock (Sync)
            {
                ActorIdList.Add(actor.Id);
                VersionList.Add(tick.DispatchTableVersion);
            }

            return default;
        }

        public static async ValueTask TickWithTimerAsync(TickActor actor, HotfixActorTick tick)
        {
            lock (Sync)
            {
                ActorIdList.Add(actor.Id);
                VersionList.Add(tick.DispatchTableVersion);
            }

            await LakonaTimer.CreateOnceTimerAsync<TimerCallbackTarget, TimerArgs>(
                TimeSpan.Zero,
                "HandleAsync",
                new TimerArgs(actor.Id)).ConfigureAwait(false);
        }

        public static async Task WaitForCountAsync(int count, CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(1));
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(5));
            while (await timer.WaitForNextTickAsync(timeout.Token).ConfigureAwait(false))
            {
                lock (Sync)
                {
                    if (ActorIdList.Count >= count)
                    {
                        return;
                    }
                }
            }
        }

        public static async Task WaitForActorAsync(string actorId, CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(1));
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(5));
            while (await timer.WaitForNextTickAsync(timeout.Token).ConfigureAwait(false))
            {
                lock (Sync)
                {
                    if (ActorIdList.Contains(actorId, StringComparer.Ordinal))
                    {
                        return;
                    }
                }
            }
        }

        public static async Task WaitForVersionAsync(long version, CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(1));
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(5));
            while (await timer.WaitForNextTickAsync(timeout.Token).ConfigureAwait(false))
            {
                lock (Sync)
                {
                    if (VersionList.Contains(version))
                    {
                        return;
                    }
                }
            }
        }

        public static void Reset()
        {
            lock (Sync)
            {
                ActorIdList.Clear();
                VersionList.Clear();
            }
        }
    }
}
