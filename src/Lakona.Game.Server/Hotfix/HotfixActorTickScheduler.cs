using System.Diagnostics;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Dispatch;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Hotfix;

internal sealed class HotfixActorTickScheduler : IAsyncDisposable
{
    private readonly IActorRuntime actors;
    private readonly ILogger<HotfixActorTickScheduler> logger;
    private readonly IHotfixRuntimeAccessor? hotfixRuntime;
    private readonly object _sync = new();
    private readonly Dictionary<string, TickLoop> _loops = [];
    private readonly Dictionary<PendingKey, PendingState> _pending = [];
    private readonly IHotfixActorTickSchedulerObserver _observer;

    public HotfixActorTickScheduler(
        IActorRuntime actors,
        ILogger<HotfixActorTickScheduler> logger)
        : this(actors, logger, hotfixRuntime: null, observer: null)
    {
    }

    public HotfixActorTickScheduler(
        IActorRuntime actors,
        ILogger<HotfixActorTickScheduler> logger,
        IHotfixActorTickSchedulerObserver? observer)
        : this(actors, logger, hotfixRuntime: null, observer)
    {
    }

    public HotfixActorTickScheduler(
        IActorRuntime actors,
        ILogger<HotfixActorTickScheduler> logger,
        IHotfixRuntimeAccessor? hotfixRuntime)
        : this(actors, logger, hotfixRuntime, observer: null)
    {
    }

    public HotfixActorTickScheduler(
        IActorRuntime actors,
        ILogger<HotfixActorTickScheduler> logger,
        IHotfixRuntimeAccessor? hotfixRuntime,
        IHotfixActorTickSchedulerObserver? observer)
    {
        this.actors = actors ?? throw new ArgumentNullException(nameof(actors));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.hotfixRuntime = hotfixRuntime;
        _observer = observer ?? NullHotfixActorTickSchedulerObserver.Instance;
    }

    public void Apply(HotfixSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var nextLoops = snapshot.Features
            .SelectMany((feature, featureIndex) => feature.ActorTicks.Select((tick, tickIndex) => new TickSource(
                $"{feature.Name}:{featureIndex}:{tickIndex}:{tick.Mode}:{tick.ActorType.FullName}:{tick.ActorId}:{tick.MethodName}",
                tick.ActorType,
                tick.ActorId,
                tick.MethodName,
                tick.Interval,
                tick.BacklogPolicy,
                tick.Mode,
                snapshot.DispatchTableVersion)))
            .ToDictionary(static source => source.Key, static source => new TickLoop(source));

        TickLoop[] stale;
        lock (_sync)
        {
            stale = _loops.Values.ToArray();
            _loops.Clear();
            foreach (var loop in nextLoops.Values)
            {
                _loops.Add(loop.Source.Key, loop);
            }

            var staleKeys = stale.Select(static loop => loop.Source.Key).ToHashSet(StringComparer.Ordinal);
            foreach (var key in _pending.Keys.Where(key => staleKeys.Contains(key.SourceKey)).ToArray())
            {
                _pending.Remove(key);
            }
        }

        foreach (var loop in stale)
        {
            loop.Cancel();
            _ = loop.DisposeAsync().AsTask();
        }

        foreach (var loop in nextLoops.Values)
        {
            loop.Start(RunLoopAsync(loop.Source, loop.Token));
        }
    }

    public async ValueTask DisposeAsync()
    {
        TickLoop[] loops;
        lock (_sync)
        {
            loops = _loops.Values.ToArray();
            _loops.Clear();
        }

        foreach (var loop in loops)
        {
            loop.Cancel();
        }

        foreach (var loop in loops)
        {
            await loop.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task RunLoopAsync(TickSource source, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            DispatchInitialTicks(source);
            using var timer = new PeriodicTimer(source.Interval);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                foreach (var actorId in ResolveActorIds(source))
                {
                    Dispatch(source, actorId);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Hotfix actor tick source {TickSource} stopped unexpectedly.", source.Key);
        }
    }

    private void DispatchInitialTicks(TickSource source)
    {
        if (source.Mode != HotfixActorTickMode.FixedActor)
        {
            return;
        }

        foreach (var actorId in ResolveActorIds(source))
        {
            Dispatch(source, actorId);
        }
    }

    private IReadOnlyList<ActorId> ResolveActorIds(TickSource source)
    {
        return source.Mode switch
        {
            HotfixActorTickMode.FixedActor => [ActorId.From(source.ActorId)],
            HotfixActorTickMode.ActiveActors => actors.GetActiveActorIds(source.ActorType),
            _ => []
        };
    }

    private void Dispatch(TickSource source, ActorId actorId)
    {
        var key = new PendingKey(source.Key, actorId);
        var observation = CreateDispatchObservation(source, actorId);
        PendingState pending;
        var skipped = false;
        var coalesced = false;
        lock (_sync)
        {
            if (_pending.TryGetValue(key, out pending!))
            {
                if (source.BacklogPolicy == TickBacklogPolicy.Coalesce)
                {
                    pending.Coalesced = true;
                    coalesced = true;
                }
                else
                {
                    skipped = true;
                }
            }
            else
            {
                pending = new PendingState();
                _pending.Add(key, pending);
            }
        }

        if (coalesced)
        {
            NotifyObserver(
                observer => observer.OnDispatchCoalesced(observation),
                "dispatch coalesced",
                observation);
            return;
        }

        if (skipped)
        {
            logger.LogDebug(
                "Skipping hotfix actor tick {TickSource} for actor {ActorId}; previous tick is pending.",
                source.Key,
                actorId.Value);
            NotifyObserver(
                observer => observer.OnDispatchSkipped(observation),
                "dispatch skipped",
                observation);
            return;
        }

        DispatchPending(source, actorId, key, pending, observation);
    }

    private void DispatchPending(
        TickSource source,
        ActorId actorId,
        PendingKey key,
        PendingState pending,
        HotfixActorTickDispatchObservation observation)
    {
        var result = actors.TryTell(
            source.ActorType,
            actorId,
            async (actor, cancellationToken) =>
            {
                try
                {
                    using var lease = hotfixRuntime?.AcquireCurrent();
                    var table = lease?.Snapshot.DispatchTable ?? HotfixDispatch.Current;
                    var tick = new HotfixActorTick
                    {
                        ObservedAtUtc = DateTime.UtcNow,
                        Interval = source.Interval,
                        Sequence = Interlocked.Increment(ref pending.Sequence),
                        DispatchTableVersion = table.Version
                    };
                    var entryObservation = new HotfixActorTickEntryObservation(
                        observation.SourceKey,
                        observation.ActorType,
                        observation.ActorId,
                        observation.MethodName,
                        observation.Interval,
                        observation.BacklogPolicy,
                        observation.QueuedTimestamp,
                        Stopwatch.GetTimestamp(),
                        tick.Sequence);
                    NotifyObserver(
                        observer => observer.OnTickEntered(entryObservation),
                        "tick entered",
                        observation);

                    await HotfixDispatch.InvokeValueTaskAsync(
                        source.ActorType,
                        source.MethodName,
                        actor,
                        [typeof(HotfixActorTick)],
                        [tick]).ConfigureAwait(false);
                }
                finally
                {
                    CompletePending(source, actorId, key, pending);
                }
            });

        if (result == ActorTellResult.Accepted)
        {
            NotifyObserver(
                observer => observer.OnDispatchAccepted(observation),
                "dispatch accepted",
                observation);
            return;
        }

        logger.LogDebug(
            "Hotfix actor tick {TickSource} for actor {ActorId} was not accepted: {Result}.",
            source.Key,
            actorId.Value,
            result);

        NotifyObserver(
            observer => observer.OnDispatchRejected(observation, result),
            "dispatch rejected",
            observation);
        CompletePending(source, actorId, key, pending);
    }

    private void CompletePending(
        TickSource source,
        ActorId actorId,
        PendingKey key,
        PendingState pending)
    {
        var dispatchFollowUp = false;
        lock (_sync)
        {
            if (!ReferenceEquals(_pending.GetValueOrDefault(key), pending))
            {
                return;
            }

            if (pending.Coalesced)
            {
                pending.Coalesced = false;
                dispatchFollowUp = true;
            }
            else
            {
                _pending.Remove(key);
            }
        }

        if (dispatchFollowUp)
        {
            DispatchPending(source, actorId, key, pending, CreateDispatchObservation(source, actorId));
        }
    }

    private static HotfixActorTickDispatchObservation CreateDispatchObservation(
        TickSource source,
        ActorId actorId)
    {
        return new HotfixActorTickDispatchObservation(
            source.Key,
            source.ActorType,
            actorId,
            source.MethodName,
            source.Interval,
            source.BacklogPolicy,
            Stopwatch.GetTimestamp());
    }

    private void NotifyObserver(
        Action<IHotfixActorTickSchedulerObserver> notify,
        string eventName,
        HotfixActorTickDispatchObservation observation)
    {
        try
        {
            notify(_observer);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Hotfix actor tick scheduler observer failed for {ObserverEvent} on tick source {TickSource}.",
                eventName,
                observation.SourceKey);
        }
    }

    private sealed record TickSource(
        string Key,
        Type ActorType,
        string ActorId,
        string MethodName,
        TimeSpan Interval,
        TickBacklogPolicy BacklogPolicy,
        HotfixActorTickMode Mode,
        long DispatchTableVersion);

    private readonly record struct PendingKey(string SourceKey, ActorId ActorId);

    private sealed class PendingState
    {
        public long Sequence;

        public bool Coalesced;
    }

    private sealed class TickLoop(TickSource source) : IAsyncDisposable
    {
        private readonly CancellationTokenSource _cancellation = new();
        private Task? _task;

        public TickSource Source { get; } = source;

        public CancellationToken Token => _cancellation.Token;

        public void Start(Task task)
        {
            _task = task;
        }

        public void Cancel()
        {
            _cancellation.Cancel();
        }

        public async ValueTask DisposeAsync()
        {
            Cancel();
            if (_task is not null)
            {
                try
                {
                    await _task.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
                {
                }
            }

            _cancellation.Dispose();
        }
    }
}
