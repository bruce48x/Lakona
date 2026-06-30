using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;
using Lakona.Game.Server.Hotfix.Dispatch;
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

    private static ScopedRuntime CreateRuntime(
        IReadOnlyList<HotfixFeatureDeclaration> features,
        string marker = "runtime",
        ILakonaTimerBackend? timerBackend = null)
    {
        var table = new HotfixDispatchTable(1, Array.Empty<HotfixMethodBinding>(), Array.Empty<HotfixServiceMethodBinding>(), features);
        var services = new ServiceCollection()
            .AddSingleton(new RuntimeMarker(marker));
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

    private sealed record RuntimeMarker(string Value);

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

    private sealed class FailingStartFeature : HotfixGameFeature
    {
        public static ValueTask StartAsync(HotfixFeatureStartCall call)
        {
            LifecycleRecorder.Events.Add("start:new-b");
            throw new InvalidOperationException("start failed");
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
