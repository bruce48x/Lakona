using System.Reflection;
using System.Runtime.Loader;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Hotfix.Loading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Server.Hotfix.Tests;

public sealed class HotfixFeatureLifecycleTests
{
    [Fact]
    public async Task Coordinator_starts_new_features_in_configured_order_and_stops_removed_features_in_reverse_order()
    {
        LifecycleRecorder.Reset();
        var coordinator = new HotfixFeatureLifecycleCoordinator();
        using var candidate = CreateRuntime([Feature("alpha", typeof(AlphaFeature)), Feature("beta", typeof(BetaFeature))]);

        var published = await coordinator.StartCandidateAsync(
            HotfixFeatureLifecycleSnapshot.Empty,
            candidate.Snapshot,
            candidate.Snapshot.DispatchTable!.Features,
            TestContext.Current.CancellationToken);
        await coordinator.CommitCandidateTimersAsync(published, TestContext.Current.CancellationToken);

        Assert.Equal(["start:alpha", "start:beta"], LifecycleRecorder.Events);
        Assert.Equal(["alpha", "beta"], published.FeatureNames);

        using var empty = CreateRuntime([]);
        var next = await coordinator.StartCandidateAsync(
            published,
            empty.Snapshot,
            empty.Snapshot.DispatchTable!.Features,
            TestContext.Current.CancellationToken);
        await coordinator.StopRemovedAsync(published, next, TestContext.Current.CancellationToken);

        Assert.Equal(["start:alpha", "start:beta", "stop:beta", "stop:alpha"], LifecycleRecorder.Events);
    }

    [Fact]
    public async Task Coordinator_preserves_same_feature_name_state_without_rerunning_start_or_stop()
    {
        LifecycleRecorder.Reset();
        var coordinator = new HotfixFeatureLifecycleCoordinator();
        using var first = CreateRuntime([Feature("same", typeof(StatefulFeature))]);
        var published = await coordinator.StartCandidateAsync(
            HotfixFeatureLifecycleSnapshot.Empty,
            first.Snapshot,
            first.Snapshot.DispatchTable!.Features,
            TestContext.Current.CancellationToken);
        published.States["same"].Items["value"] = 42;

        using var second = CreateRuntime([Feature("same", typeof(StatefulFeatureV2))]);
        var reloaded = await coordinator.StartCandidateAsync(
            published,
            second.Snapshot,
            second.Snapshot.DispatchTable!.Features,
            TestContext.Current.CancellationToken);
        await coordinator.StopRemovedAsync(published, reloaded, TestContext.Current.CancellationToken);

        Assert.Equal(["start:same"], LifecycleRecorder.Events);
        Assert.Same(published.States["same"], reloaded.States["same"]);
        Assert.Equal(42, reloaded.States["same"].Items["value"]);
    }

    [Fact]
    public async Task Removed_feature_stop_runs_under_previous_snapshot()
    {
        LifecycleRecorder.Reset();
        var coordinator = new HotfixFeatureLifecycleCoordinator();
        using var previous = CreateRuntime([Feature("removed", typeof(SnapshotObservingFeature))], "previous");
        var published = await coordinator.StartCandidateAsync(
            HotfixFeatureLifecycleSnapshot.Empty,
            previous.Snapshot,
            previous.Snapshot.DispatchTable!.Features,
            TestContext.Current.CancellationToken);

        using var next = CreateRuntime([], "candidate");
        var candidate = await coordinator.StartCandidateAsync(
            published,
            next.Snapshot,
            next.Snapshot.DispatchTable!.Features,
            TestContext.Current.CancellationToken);
        await coordinator.StopRemovedAsync(published, candidate, TestContext.Current.CancellationToken);

        Assert.Equal("previous", LifecycleRecorder.StopServiceMarker);
    }

    [Fact]
    public async Task Renamed_feature_stops_old_name_under_previous_snapshot_and_starts_new_name()
    {
        LifecycleRecorder.Reset();
        var coordinator = new HotfixFeatureLifecycleCoordinator();
        using var previousRuntime = CreateRuntime([Feature("old-name", typeof(SnapshotObservingFeature))], "previous");
        var previous = await coordinator.StartCandidateAsync(
            HotfixFeatureLifecycleSnapshot.Empty,
            previousRuntime.Snapshot,
            previousRuntime.Snapshot.DispatchTable!.Features,
            TestContext.Current.CancellationToken);

        LifecycleRecorder.Reset();
        using var candidateRuntime = CreateRuntime([Feature("new-name", typeof(StatefulFeature))], "candidate");
        var next = await coordinator.StartCandidateAsync(
            previous,
            candidateRuntime.Snapshot,
            candidateRuntime.Snapshot.DispatchTable!.Features,
            TestContext.Current.CancellationToken);
        await coordinator.StopRemovedAsync(previous, next, TestContext.Current.CancellationToken);

        Assert.Equal(["start:new-name"], LifecycleRecorder.Events);
        Assert.Equal("previous", LifecycleRecorder.StopServiceMarker);
        Assert.DoesNotContain("old-name", next.FeatureNames);
        Assert.Contains("new-name", next.FeatureNames);
    }

    [Fact]
    public async Task Start_failure_stops_already_started_candidates_rolls_back_timers_and_preserves_old_state()
    {
        LifecycleRecorder.Reset();
        var coordinator = new HotfixFeatureLifecycleCoordinator();
        using var oldRuntime = CreateRuntime([Feature("old", typeof(StatefulFeature))]);
        var oldState = HotfixFeatureLifecycleSnapshot.Empty.WithStartedFeature(
            Feature("old", typeof(StatefulFeature)),
            new HotfixFeatureState { Items = { ["value"] = 7 } },
            oldRuntime.Snapshot);
        var backend = new RecordingLifecycleTimerBackend();
        using var candidate = CreateRuntime(
            [Feature("new-a", typeof(TimerStartFeature)), Feature("new-b", typeof(FailingStartFeature))],
            timerBackend: backend);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await coordinator.StartCandidateAsync(
                oldState,
                candidate.Snapshot,
                candidate.Snapshot.DispatchTable!.Features,
                TestContext.Current.CancellationToken));

        Assert.Equal("start failed", ex.Message);
        Assert.Equal(["start:new-a", "start:new-b", "stop:new-a"], LifecycleRecorder.Events);
        Assert.Equal(1, backend.StagedCreateCount);
        Assert.Equal(1, backend.RollbackCount);
        Assert.Equal(0, backend.CommitCount);
        Assert.Equal(7, oldState.States["old"].Items["value"]);
        Assert.Empty(backend.ActiveTimers);
    }

    [Fact]
    public async Task Start_failure_rolls_back_timers_even_when_candidate_stop_fails_and_preserves_original_failure()
    {
        LifecycleRecorder.Reset();
        var coordinator = new HotfixFeatureLifecycleCoordinator();
        var backend = new RecordingLifecycleTimerBackend();
        using var candidate = CreateRuntime(
            [Feature("new-a", typeof(ThrowingStopTimerStartFeature)), Feature("new-b", typeof(FailingStartFeature))],
            timerBackend: backend);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await coordinator.StartCandidateAsync(
                HotfixFeatureLifecycleSnapshot.Empty,
                candidate.Snapshot,
                candidate.Snapshot.DispatchTable!.Features,
                TestContext.Current.CancellationToken));

        Assert.Equal("start failed", ex.Message);
        Assert.Equal(["start:throwing-stop", "start:new-b", "stop:throwing-stop"], LifecycleRecorder.Events);
        Assert.Equal(1, backend.StagedCreateCount);
        Assert.Equal(1, backend.RollbackCount);
        Assert.Empty(backend.ActiveTimers);
    }

    [Fact]
    public async Task Start_failure_rolls_back_timers_created_by_nested_candidate_dispatch()
    {
        LifecycleRecorder.Reset();
        var coordinator = new HotfixFeatureLifecycleCoordinator();
        var backend = new RecordingLifecycleTimerBackend();
        using var candidate = CreateRuntime(
            [Feature("new-a", typeof(NestedDispatchTimerStartFeature)), Feature("new-b", typeof(FailingStartFeature))],
            timerBackend: backend,
            serviceBindings: [CreateNestedStartTimerServiceBinding()]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await coordinator.StartCandidateAsync(
                HotfixFeatureLifecycleSnapshot.Empty,
                candidate.Snapshot,
                candidate.Snapshot.DispatchTable!.Features,
                TestContext.Current.CancellationToken));

        Assert.Equal("start failed", ex.Message);
        Assert.Equal(["start:nested-dispatch", "service:nested-dispatch", "start:new-b", "stop:nested-dispatch"], LifecycleRecorder.Events);
        Assert.Equal(1, backend.StagedCreateCount);
        Assert.Equal(1, backend.RollbackCount);
        Assert.Empty(backend.ActiveTimers);
    }

    [Fact]
    public async Task Disabled_candidate_feature_does_not_start()
    {
        LifecycleRecorder.Reset();
        var coordinator = new HotfixFeatureLifecycleCoordinator();
        var policy = new ConfiguredHotfixFeatureActivationPolicy(["enabled"]);
        using var candidate = CreateRuntime([Feature("enabled", typeof(AlphaFeature)), Feature("disabled", typeof(BetaFeature))]);

        var published = await coordinator.StartCandidateAsync(
            HotfixFeatureLifecycleSnapshot.Empty,
            candidate.Snapshot,
            policy.SelectActiveFeatures(candidate.Snapshot.DispatchTable!.Features),
            TestContext.Current.CancellationToken);

        Assert.Equal(["start:alpha"], LifecycleRecorder.Events);
        Assert.Equal(["enabled"], published.FeatureNames);
        Assert.True(published.States.ContainsKey("enabled"));
        Assert.False(published.States.ContainsKey("disabled"));
    }

    [Fact]
    public async Task Previously_active_feature_stops_when_disabled_under_previous_snapshot()
    {
        LifecycleRecorder.Reset();
        var coordinator = new HotfixFeatureLifecycleCoordinator();
        using var previousRuntime = CreateRuntime([Feature("kept", typeof(AlphaFeature)), Feature("disabled", typeof(SnapshotObservingFeature))], "previous");
        var previous = await coordinator.StartCandidateAsync(
            HotfixFeatureLifecycleSnapshot.Empty,
            previousRuntime.Snapshot,
            previousRuntime.Snapshot.DispatchTable!.Features,
            TestContext.Current.CancellationToken);

        using var candidateRuntime = CreateRuntime([Feature("kept", typeof(AlphaFeature)), Feature("disabled", typeof(SnapshotObservingFeature))], "candidate");
        var policy = new ConfiguredHotfixFeatureActivationPolicy(["kept"]);
        var next = await coordinator.StartCandidateAsync(
            previous,
            candidateRuntime.Snapshot,
            policy.SelectActiveFeatures(candidateRuntime.Snapshot.DispatchTable!.Features),
            TestContext.Current.CancellationToken);
        await coordinator.StopRemovedAsync(previous, next, TestContext.Current.CancellationToken);

        Assert.Equal("previous", LifecycleRecorder.StopServiceMarker);
        Assert.DoesNotContain("disabled", next.FeatureNames);
    }

    [Fact]
    public async Task Publication_participant_failure_before_publish_keeps_old_publication_and_rolls_back_candidate()
    {
        LifecycleRecorder.Reset();
        using var oldRuntime = CreateRuntime([Feature("old", typeof(StatefulFeature))], "old");
        using var candidateRuntime = CreateRuntime(
            [Feature("new-a", typeof(TimerStartFeature))],
            "candidate",
            new RecordingLifecycleTimerBackend());
        var participant = new FailingBeforePublishParticipant();
        var manager = new HotfixManager(
            new UnusedAssemblySource(),
            participants: [participant]);
        var firstSnapshot = CreateSnapshot(1, oldRuntime.Snapshot.DispatchTable!.Features);
        await manager.PublishCandidateAsync(
            oldRuntime.Snapshot,
            firstSnapshot,
            oldRuntime.Snapshot.DispatchTable!.Features,
            TestContext.Current.CancellationToken);
        LifecycleRecorder.Reset();
        participant.Fail = true;
        var oldPublishedRuntime = ((IHotfixRuntimeAccessor)manager).Current;
        var oldPublishedSnapshot = manager.Current;
        var oldPublishedDispatch = HotfixDispatch.Current;

        var failed = await manager.PublishCandidateAsync(
            candidateRuntime.Snapshot,
            CreateSnapshot(2, candidateRuntime.Snapshot.DispatchTable!.Features),
            candidateRuntime.Snapshot.DispatchTable!.Features,
            TestContext.Current.CancellationToken);

        var backend = Assert.IsType<RecordingLifecycleTimerBackend>(
            candidateRuntime.Snapshot.Services.GetRequiredService<ILakonaTimerBackend>());
        Assert.False(failed.Succeeded);
        Assert.Same(oldPublishedRuntime, ((IHotfixRuntimeAccessor)manager).Current);
        Assert.Same(oldPublishedSnapshot, manager.Current);
        Assert.Same(oldPublishedDispatch, HotfixDispatch.Current);
        Assert.Equal(1, backend.RollbackCount);
        Assert.Equal(["start:new-a", "stop:new-a"], LifecycleRecorder.Events);
    }

    [Fact]
    public async Task Publication_participant_failure_rolls_back_timers_even_when_candidate_stop_fails()
    {
        LifecycleRecorder.Reset();
        using var oldRuntime = CreateRuntime([Feature("old", typeof(StatefulFeature))], "old");
        var backend = new RecordingLifecycleTimerBackend();
        using var candidateRuntime = CreateRuntime(
            [Feature("new-a", typeof(ThrowingStopTimerStartFeature))],
            "candidate",
            backend);
        var participant = new FailingBeforePublishParticipant();
        var manager = new HotfixManager(
            new UnusedAssemblySource(),
            participants: [participant]);
        var first = await manager.PublishCandidateAsync(
            oldRuntime.Snapshot,
            CreateSnapshot(1, oldRuntime.Snapshot.DispatchTable!.Features),
            oldRuntime.Snapshot.DispatchTable!.Features,
            TestContext.Current.CancellationToken);
        Assert.True(first.Succeeded, string.Join(Environment.NewLine, first.Diagnostics));
        LifecycleRecorder.Reset();
        participant.Fail = true;
        var oldPublishedRuntime = ((IHotfixRuntimeAccessor)manager).Current;
        var oldPublishedSnapshot = manager.Current;
        var oldPublishedDispatch = HotfixDispatch.Current;

        var failed = await manager.PublishCandidateAsync(
            candidateRuntime.Snapshot,
            CreateSnapshot(2, candidateRuntime.Snapshot.DispatchTable!.Features),
            candidateRuntime.Snapshot.DispatchTable!.Features,
            TestContext.Current.CancellationToken);

        Assert.False(failed.Succeeded);
        Assert.Contains("participant failed", failed.Diagnostics);
        Assert.Same(oldPublishedRuntime, ((IHotfixRuntimeAccessor)manager).Current);
        Assert.Same(oldPublishedSnapshot, manager.Current);
        Assert.Same(oldPublishedDispatch, HotfixDispatch.Current);
        Assert.Equal(1, backend.RollbackCount);
        Assert.Empty(backend.ActiveTimers);
        Assert.Equal(["start:throwing-stop", "stop:throwing-stop"], LifecycleRecorder.Events);
    }

    [Fact]
    public async Task Publication_participants_observe_old_state_before_publish_and_consistent_new_state_after_publish()
    {
        LifecycleRecorder.Reset();
        using var oldRuntime = CreateRuntime([Feature("old", typeof(StatefulFeature))], "1");
        using var candidateRuntime = CreateRuntime([Feature("new-a", typeof(AlphaFeature))], "2");
        var observations = new PublicationObservationParticipant();
        var manager = new HotfixManager(new UnusedAssemblySource(), participants: [observations]);
        observations.CurrentManager = manager;

        var first = await manager.PublishCandidateAsync(
            oldRuntime.Snapshot,
            CreateSnapshot(1, oldRuntime.Snapshot.DispatchTable!.Features),
            oldRuntime.Snapshot.DispatchTable!.Features,
            TestContext.Current.CancellationToken);
        Assert.True(first.Succeeded, string.Join(Environment.NewLine, first.Diagnostics));
        observations.Enabled = true;
        observations.Reset();

        var second = await manager.PublishCandidateAsync(
            candidateRuntime.Snapshot,
            CreateSnapshot(2, candidateRuntime.Snapshot.DispatchTable!.Features),
            candidateRuntime.Snapshot.DispatchTable!.Features,
            TestContext.Current.CancellationToken);
        Assert.True(second.Succeeded, string.Join(Environment.NewLine, second.Diagnostics));

        Assert.Equal(1, observations.BeforeSnapshotVersion);
        Assert.Equal(1, observations.BeforeRuntimeVersion);
        Assert.Equal(1, observations.BeforeDispatchVersion);
        Assert.Equal(2, observations.AfterSnapshotVersion);
        Assert.Equal(2, observations.AfterRuntimeVersion);
        Assert.Equal(2, observations.AfterDispatchVersion);
    }

    [Fact]
    public async Task Publication_participants_can_lease_previous_runtime_during_after_publish()
    {
        using var oldRuntime = CreateRuntime([Feature("old", typeof(StatefulFeature))], "1");
        using var candidateRuntime = CreateRuntime([Feature("new-a", typeof(AlphaFeature))], "2");
        var participant = new PreviousRuntimeLeaseParticipant();
        var manager = new HotfixManager(new UnusedAssemblySource(), participants: [participant]);

        var first = await manager.PublishCandidateAsync(
            oldRuntime.Snapshot,
            CreateSnapshot(1, oldRuntime.Snapshot.DispatchTable!.Features),
            oldRuntime.Snapshot.DispatchTable!.Features,
            TestContext.Current.CancellationToken);
        Assert.True(first.Succeeded, string.Join(Environment.NewLine, first.Diagnostics));
        participant.Enabled = true;

        var second = await manager.PublishCandidateAsync(
            candidateRuntime.Snapshot,
            CreateSnapshot(2, candidateRuntime.Snapshot.DispatchTable!.Features),
            candidateRuntime.Snapshot.DispatchTable!.Features,
            TestContext.Current.CancellationToken);

        Assert.True(second.Succeeded, string.Join(Environment.NewLine, second.Diagnostics));
        Assert.True(participant.LeasedPreviousRuntime);
    }

    [Fact]
    public async Task Staged_timers_are_committed_only_after_candidate_publication_is_current()
    {
        LifecycleRecorder.Reset();
        using var oldRuntime = CreateRuntime([Feature("old", typeof(StatefulFeature))], "1");
        var backend = new RecordingLifecycleTimerBackend();
        using var candidateRuntime = CreateRuntime([Feature("new-a", typeof(TimerStartFeature))], "2", backend);
        var manager = new HotfixManager(new UnusedAssemblySource());
        backend.ReadCurrentVersionDuringCommit = () => manager.Current.Version;

        var first = await manager.PublishCandidateAsync(
            oldRuntime.Snapshot,
            CreateSnapshot(1, oldRuntime.Snapshot.DispatchTable!.Features),
            oldRuntime.Snapshot.DispatchTable!.Features,
            TestContext.Current.CancellationToken);
        Assert.True(first.Succeeded, string.Join(Environment.NewLine, first.Diagnostics));
        LifecycleRecorder.Reset();

        var second = await manager.PublishCandidateAsync(
            candidateRuntime.Snapshot,
            CreateSnapshot(2, candidateRuntime.Snapshot.DispatchTable!.Features),
            candidateRuntime.Snapshot.DispatchTable!.Features,
            TestContext.Current.CancellationToken);

        Assert.True(second.Succeeded, string.Join(Environment.NewLine, second.Diagnostics));
        Assert.Equal("2", backend.CurrentVersionObservedDuringCommit);
        Assert.Equal(1, backend.CommitCount);
        Assert.Single(backend.ActiveTimers);
    }

    [Fact]
    public async Task Hotfix_owned_feature_state_value_rejects_candidate_publication_and_keeps_old_publication()
    {
        LifecycleRecorder.Reset();
        using var oldRuntime = CreateRuntime([Feature("old", typeof(StatefulFeature))], "1");
        using var candidateRuntime = CreateRuntime([Feature("state", typeof(CollectibleStateFeature))], "2");
        var manager = new HotfixManager(new UnusedAssemblySource());
        var first = await manager.PublishCandidateAsync(
            oldRuntime.Snapshot,
            CreateSnapshot(1, oldRuntime.Snapshot.DispatchTable!.Features),
            oldRuntime.Snapshot.DispatchTable!.Features,
            TestContext.Current.CancellationToken);
        Assert.True(first.Succeeded, string.Join(Environment.NewLine, first.Diagnostics));
        var oldPublishedRuntime = ((IHotfixRuntimeAccessor)manager).Current;
        var oldPublishedSnapshot = manager.Current;
        var oldPublishedDispatch = HotfixDispatch.Current;
        var stateValue = CreateCollectibleStateValue(out var loadContext);
        CollectibleStateFeature.Value = stateValue;

        try
        {
            var failed = await manager.PublishCandidateAsync(
                candidateRuntime.Snapshot,
                CreateSnapshot(2, candidateRuntime.Snapshot.DispatchTable!.Features),
                candidateRuntime.Snapshot.DispatchTable!.Features,
                TestContext.Current.CancellationToken);

            Assert.False(failed.Succeeded);
            Assert.Contains(failed.Diagnostics, diagnostic =>
                diagnostic.Contains("HotfixFeatureState", StringComparison.Ordinal) &&
                diagnostic.Contains("reload-safe", StringComparison.OrdinalIgnoreCase));
            Assert.Same(oldPublishedRuntime, ((IHotfixRuntimeAccessor)manager).Current);
            Assert.Same(oldPublishedSnapshot, manager.Current);
            Assert.Same(oldPublishedDispatch, HotfixDispatch.Current);
        }
        finally
        {
            CollectibleStateFeature.Value = null;
            loadContext.Unload();
        }
    }

    private static ScopedRuntime CreateRuntime(
        IReadOnlyList<HotfixFeatureDeclaration> features,
        string marker = "runtime",
        ILakonaTimerBackend? timerBackend = null,
        IReadOnlyList<HotfixServiceMethodBinding>? serviceBindings = null)
    {
        var tableVersion = long.TryParse(marker, out var parsedVersion) ? parsedVersion : 1;
        var table = new HotfixDispatchTable(
            tableVersion,
            Array.Empty<HotfixMethodBinding>(),
            serviceBindings ?? Array.Empty<HotfixServiceMethodBinding>(),
            features);
        var services = new ServiceCollection()
            .AddSingleton(new RuntimeMarker(marker))
            .AddSingleton<IHotfixServiceInvoker>(new HotfixServiceInvoker(table));
        if (timerBackend is not null)
        {
            services.AddSingleton(timerBackend);
        }

        var provider = services.BuildServiceProvider();
        var snapshot = new HotfixRuntimeSnapshot(
            new HotfixServiceInvoker(table),
            EmptyHotfixFeatureCommandInvoker.Instance,
            provider,
            table,
            provider,
            mainAssembly: typeof(HotfixFeatureLifecycleTests).Assembly,
            loadContext: null,
            sourceVersion: marker,
            sourceKind: null,
            sourcePath: null,
            ownsRuntimeResources: false,
            onRetired: null);
        return new ScopedRuntime(provider, snapshot);
    }

    private static HotfixServiceMethodBinding CreateNestedStartTimerServiceBinding()
    {
        var method = typeof(NestedStartTimerService).GetMethod(nameof(NestedStartTimerService.CreateTimerAsync))!;
        return new HotfixServiceMethodBinding(
            HotfixDispatch.CreateServiceKey(
                typeof(INestedStartTimerContract),
                71,
                typeof(ValueTask),
                [typeof(HotfixServiceCall<TimerArgs>)]),
            method,
            typeof(NestedStartTimerService),
            typeof(INestedStartTimerContract),
            typeof(ValueTask),
            [typeof(HotfixServiceCall<TimerArgs>)]);
    }

    private static HotfixFeatureDeclaration Feature(string name, Type type)
    {
        return new HotfixFeatureDeclaration(
            name,
            type,
            true,
            new Dictionary<string, string>(StringComparer.Ordinal),
            [],
            [],
            [],
            [],
            HotfixFeatureLifecycleDeclaration.FromFeatureType(type));
    }

    private static HotfixSnapshot CreateSnapshot(long tableVersion, IReadOnlyList<HotfixFeatureDeclaration> features)
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

    private static object CreateCollectibleStateValue(out AssemblyLoadContext loadContext)
    {
        var assemblyName = $"HotfixStateValue_{Guid.NewGuid():N}";
        var syntaxTree = CSharpSyntaxTree.ParseText(
            """
            namespace HotfixStateValue;

            public sealed class Payload
            {
            }
            """);
        var references = GetTrustedPlatformReferences()
            .GroupBy(static reference => reference.Display, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        if (!emit.Success)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, emit.Diagnostics));
        }

        stream.Position = 0;
        loadContext = new AssemblyLoadContext(assemblyName, isCollectible: true);
        var assembly = loadContext.LoadFromStream(stream);
        return Activator.CreateInstance(assembly.GetType("HotfixStateValue.Payload", throwOnError: true)!)!;
    }

    private static IEnumerable<MetadataReference> GetTrustedPlatformReferences()
    {
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        if (trustedPlatformAssemblies is null)
        {
            throw new InvalidOperationException("TRUSTED_PLATFORM_ASSEMBLIES is not available.");
        }

        return trustedPlatformAssemblies
            .Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path));
    }

    private sealed record RuntimeMarker(string Value);

    private sealed class ConfiguredHotfixFeatureActivationPolicy(IReadOnlyList<string> names) : IHotfixFeatureActivationPolicy
    {
        public IReadOnlyList<HotfixFeatureDeclaration> SelectActiveFeatures(IReadOnlyList<HotfixFeatureDeclaration> scannedFeatures)
        {
            var allowed = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
            return scannedFeatures
                .Where(feature => allowed.Contains(feature.Name))
                .ToArray();
        }
    }

    private sealed class FailingBeforePublishParticipant : IHotfixRuntimePublicationParticipant
    {
        public bool Fail { get; set; }

        public ValueTask BeforePublishAsync(HotfixRuntimeSnapshot candidate, CancellationToken cancellationToken = default)
        {
            if (Fail)
            {
                throw new InvalidOperationException("participant failed");
            }

            return default;
        }
    }

    private sealed class PublicationObservationParticipant : IHotfixRuntimePublicationParticipant
    {
        public int? BeforeSnapshotVersion { get; private set; }

        public int? BeforeRuntimeVersion { get; private set; }

        public int? BeforeDispatchVersion { get; private set; }

        public int? AfterSnapshotVersion { get; private set; }

        public int? AfterRuntimeVersion { get; private set; }

        public int? AfterDispatchVersion { get; private set; }

        public bool Enabled { get; set; }

        public void Reset()
        {
            BeforeSnapshotVersion = null;
            BeforeRuntimeVersion = null;
            BeforeDispatchVersion = null;
            AfterSnapshotVersion = null;
            AfterRuntimeVersion = null;
            AfterDispatchVersion = null;
        }

        public ValueTask BeforePublishAsync(HotfixRuntimeSnapshot candidate, CancellationToken cancellationToken = default)
        {
            _ = candidate;
            if (!Enabled)
            {
                return default;
            }

            BeforeSnapshotVersion = int.Parse(CurrentManager!.Current.Version!, System.Globalization.CultureInfo.InvariantCulture);
            BeforeRuntimeVersion = int.Parse(((IHotfixRuntimeAccessor)CurrentManager).Current.SourceVersion!, System.Globalization.CultureInfo.InvariantCulture);
            BeforeDispatchVersion = (int)HotfixDispatch.Current.Version;
            return default;
        }

        public ValueTask AfterPublishAsync(HotfixRuntimeSnapshot previous, HotfixRuntimeSnapshot current, CancellationToken cancellationToken = default)
        {
            _ = previous;
            _ = current;
            if (!Enabled)
            {
                return default;
            }

            AfterSnapshotVersion = int.Parse(CurrentManager!.Current.Version!, System.Globalization.CultureInfo.InvariantCulture);
            AfterRuntimeVersion = int.Parse(((IHotfixRuntimeAccessor)CurrentManager).Current.SourceVersion!, System.Globalization.CultureInfo.InvariantCulture);
            AfterDispatchVersion = (int)HotfixDispatch.Current.Version;
            return default;
        }

        public HotfixManager? CurrentManager { get; set; }
    }

    private sealed class PreviousRuntimeLeaseParticipant : IHotfixRuntimePublicationParticipant
    {
        public bool Enabled { get; set; }

        public bool LeasedPreviousRuntime { get; private set; }

        public ValueTask AfterPublishAsync(HotfixRuntimeSnapshot previous, HotfixRuntimeSnapshot current, CancellationToken cancellationToken = default)
        {
            _ = current;
            _ = cancellationToken;
            if (!Enabled)
            {
                return default;
            }

            using var lease = previous.AcquireLease();
            LeasedPreviousRuntime = ReferenceEquals(previous, lease.Snapshot);
            return default;
        }
    }

    private sealed class UnusedAssemblySource : IHotfixAssemblySource
    {
        public ValueTask<HotfixAssemblySourceResult> ResolveAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private static class LifecycleRecorder
    {
        public static List<string> Events { get; } = [];

        public static string? StopServiceMarker { get; set; }

        public static void Reset()
        {
            Events.Clear();
            StopServiceMarker = null;
        }
    }

    private sealed class AlphaFeature : HotfixGameFeature
    {
        public static ValueTask StartAsync(HotfixFeatureStartCall call)
        {
            LifecycleRecorder.Events.Add("start:alpha");
            return default;
        }

        public static ValueTask StopAsync(HotfixFeatureStopCall call)
        {
            LifecycleRecorder.Events.Add("stop:alpha");
            return default;
        }
    }

    private sealed class BetaFeature : HotfixGameFeature
    {
        public static ValueTask StartAsync(HotfixFeatureStartCall call)
        {
            LifecycleRecorder.Events.Add("start:beta");
            return default;
        }

        public static ValueTask StopAsync(HotfixFeatureStopCall call)
        {
            LifecycleRecorder.Events.Add("stop:beta");
            return default;
        }
    }

    private sealed class StatefulFeature : HotfixGameFeature
    {
        public static ValueTask StartAsync(HotfixFeatureStartCall call)
        {
            LifecycleRecorder.Events.Add($"start:{call.FeatureName}");
            return default;
        }

        public static ValueTask StopAsync(HotfixFeatureStopCall call)
        {
            LifecycleRecorder.Events.Add($"stop:{call.FeatureName}");
            return default;
        }
    }

    private sealed class StatefulFeatureV2 : HotfixGameFeature
    {
        public static ValueTask StartAsync(HotfixFeatureStartCall call)
        {
            LifecycleRecorder.Events.Add($"start-v2:{call.FeatureName}");
            return default;
        }

        public static ValueTask StopAsync(HotfixFeatureStopCall call)
        {
            LifecycleRecorder.Events.Add($"stop-v2:{call.FeatureName}");
            return default;
        }
    }

    private sealed class SnapshotObservingFeature : HotfixGameFeature
    {
        public static ValueTask StartAsync(HotfixFeatureStartCall call)
        {
            return default;
        }

        public static ValueTask StopAsync(HotfixFeatureStopCall call)
        {
            LifecycleRecorder.StopServiceMarker = call.Services.GetRequiredService<RuntimeMarker>().Value;
            return default;
        }
    }

    private sealed class TimerStartFeature : HotfixGameFeature
    {
        public static async ValueTask StartAsync(HotfixFeatureStartCall call)
        {
            LifecycleRecorder.Events.Add("start:new-a");
            await LakonaTimer.CreateOnceTimerAsync<TimerCallbackTarget, TimerArgs>(
                TimeSpan.Zero,
                nameof(TimerCallbackTarget.TickAsync),
                new TimerArgs("candidate"),
                call.CancellationToken).ConfigureAwait(false);
        }

        public static ValueTask StopAsync(HotfixFeatureStopCall call)
        {
            LifecycleRecorder.Events.Add("stop:new-a");
            return default;
        }
    }

    private sealed class NestedDispatchTimerStartFeature : HotfixGameFeature
    {
        public static async ValueTask StartAsync(HotfixFeatureStartCall call)
        {
            LifecycleRecorder.Events.Add("start:nested-dispatch");
            var invoker = call.Services.GetRequiredService<IHotfixServiceInvoker>();
            await invoker.InvokeAsync<INestedStartTimerContract, HotfixServiceCall<TimerArgs>>(
                71,
                new HotfixServiceCall<TimerArgs>(new TimerArgs("nested-dispatch"), call.Services),
                call.CancellationToken).ConfigureAwait(false);
        }

        public static ValueTask StopAsync(HotfixFeatureStopCall call)
        {
            LifecycleRecorder.Events.Add("stop:nested-dispatch");
            return default;
        }
    }

    private interface INestedStartTimerContract
    {
    }

    private sealed class NestedStartTimerService
    {
        public static async ValueTask CreateTimerAsync(HotfixServiceCall<TimerArgs> call)
        {
            LifecycleRecorder.Events.Add($"service:{call.Request!.Value}");
            await LakonaTimer.CreateOnceTimerAsync<TimerCallbackTarget, TimerArgs>(
                TimeSpan.Zero,
                nameof(TimerCallbackTarget.TickAsync),
                call.Request,
                CancellationToken.None).ConfigureAwait(false);
        }
    }

    private sealed class ThrowingStopTimerStartFeature : HotfixGameFeature
    {
        public static async ValueTask StartAsync(HotfixFeatureStartCall call)
        {
            LifecycleRecorder.Events.Add("start:throwing-stop");
            await LakonaTimer.CreateOnceTimerAsync<TimerCallbackTarget, TimerArgs>(
                TimeSpan.Zero,
                nameof(TimerCallbackTarget.TickAsync),
                new TimerArgs("candidate"),
                call.CancellationToken).ConfigureAwait(false);
        }

        public static ValueTask StopAsync(HotfixFeatureStopCall call)
        {
            LifecycleRecorder.Events.Add("stop:throwing-stop");
            throw new InvalidOperationException("stop failed");
        }
    }

    private sealed class FailingStartFeature : HotfixGameFeature
    {
        public static ValueTask StartAsync(HotfixFeatureStartCall call)
        {
            LifecycleRecorder.Events.Add("start:new-b");
            throw new InvalidOperationException("start failed");
        }
    }

    private sealed class CollectibleStateFeature : HotfixGameFeature
    {
        public static object? Value { get; set; }

        public static ValueTask StartAsync(HotfixFeatureStartCall call)
        {
            call.State.Items["payload"] = Value;
            return default;
        }
    }

    private sealed class TimerCallbackTarget
    {
        public static ValueTask TickAsync(TimerTick<TimerArgs> tick)
        {
            return default;
        }
    }

    private sealed record TimerArgs(string Value);

    private sealed class RecordingLifecycleTimerBackend : ILakonaTimerBackend
    {
        private readonly List<TimerId> activeTimers = [];

        public IReadOnlyList<TimerId> ActiveTimers => activeTimers;

        public int StagedCreateCount { get; private set; }

        public int CommitCount { get; private set; }

        public int RollbackCount { get; private set; }

        public Func<string?>? ReadCurrentVersionDuringCommit { get; set; }

        public string? CurrentVersionObservedDuringCommit { get; private set; }

        public ValueTask<TimerId> CreateOnceTimerAsync<TCallback, TArgs>(
            TimeSpan dueTime,
            string methodName,
            TArgs args,
            CancellationToken cancellationToken)
            where TCallback : class
        {
            var id = TimerId.FromGuid(Guid.NewGuid());
            activeTimers.Add(id);
            return new ValueTask<TimerId>(id);
        }

        public ValueTask<TimerId> CreatePeriodicTimerAsync<TCallback, TArgs>(
            TimeSpan dueTime,
            TimeSpan period,
            string methodName,
            TArgs args,
            CancellationToken cancellationToken)
            where TCallback : class
        {
            return CreateOnceTimerAsync<TCallback, TArgs>(dueTime, methodName, args, cancellationToken);
        }

        public ValueTask DestroyTimerAsync(TimerId timerId, CancellationToken cancellationToken)
        {
            activeTimers.Remove(timerId);
            return default;
        }

        public ILakonaTimerBackend CreateStagingBackend()
        {
            return new StagingBackend(this);
        }

        public ValueTask CommitStagedTimersAsync(ILakonaTimerBackend stagingBackend, CancellationToken cancellationToken)
        {
            CommitCount++;
            CurrentVersionObservedDuringCommit = ReadCurrentVersionDuringCommit?.Invoke();
            foreach (var timerId in ((StagingBackend)stagingBackend).Timers)
            {
                activeTimers.Add(timerId);
            }

            return default;
        }

        public ValueTask RollbackStagedTimersAsync(ILakonaTimerBackend stagingBackend, CancellationToken cancellationToken)
        {
            RollbackCount++;
            ((StagingBackend)stagingBackend).Timers.Clear();
            return default;
        }

        private sealed class StagingBackend(RecordingLifecycleTimerBackend owner) : ILakonaTimerBackend
        {
            public List<TimerId> Timers { get; } = [];

            public ValueTask<TimerId> CreateOnceTimerAsync<TCallback, TArgs>(
                TimeSpan dueTime,
                string methodName,
                TArgs args,
                CancellationToken cancellationToken)
                where TCallback : class
            {
                owner.StagedCreateCount++;
                var id = TimerId.FromGuid(Guid.NewGuid());
                Timers.Add(id);
                return new ValueTask<TimerId>(id);
            }

            public ValueTask<TimerId> CreatePeriodicTimerAsync<TCallback, TArgs>(
                TimeSpan dueTime,
                TimeSpan period,
                string methodName,
                TArgs args,
                CancellationToken cancellationToken)
                where TCallback : class
            {
                return CreateOnceTimerAsync<TCallback, TArgs>(dueTime, methodName, args, cancellationToken);
            }

            public ValueTask DestroyTimerAsync(TimerId timerId, CancellationToken cancellationToken)
            {
                Timers.Remove(timerId);
                return default;
            }
        }
    }
}
