using System.Reflection;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Hotfix.Loading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using GameActor = Lakona.Game.Server.Actors.Actor;

namespace Lakona.Game.Server.Tests;

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
        var feature = new HotfixFeatureDeclaration(
            featureName,
            typeof(TestFeature),
            Discoverable: true,
            new Dictionary<string, string>(),
            ticks,
            []);
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

    private sealed class RecordingActorRuntime : IActorRuntime
    {
        private readonly Dictionary<ActorId, TickActor> _actors = [];
        private readonly object _sync = new();
        private TaskCompletionSource? _blockedRelease;
        private int _queuedWhileBlocked;

        public Dictionary<Type, IReadOnlyList<ActorId>> ActiveActorIds { get; } = [];

        public ActorId? BlockedActorId { get; set; }

        public TaskCompletionSource BlockedStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int MaxQueuedWhileBlocked { get; private set; }

        public ValueTask<TActor> GetOrCreateAsync<TActor>(ActorId id, CancellationToken cancellationToken = default)
            where TActor : class, IActor
        {
            return new ValueTask<TActor>((TActor)(IActor)GetOrCreate(id));
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
            return default;
        }

        public ValueTask<ActorStopOutcome> StopAsync(ActorId id, TimeSpan drainTimeout)
        {
            _actors.Remove(id);
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
    }

    public sealed class TickActor(string id) : GameActor
    {
        public string Id { get; } = id;
    }

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
