using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Hotfix.Timers;

internal sealed class LakonaTimerScheduler : IHostedService, IAsyncDisposable, IDisposable
{
    // Re-arming for ordinary sub-millisecond timer-construction drift creates churn without
    // materially improving due-time accuracy. Larger drift is corrected against the absolute due time.
    private static readonly TimeSpan DelayArmingDriftTolerance = TimeSpan.FromMilliseconds(1);
    private static readonly TimeSpan MaximumDelay = TimeSpan.FromMilliseconds(uint.MaxValue - 1);
    private const int MinimumStaleHeapEntriesBeforeCompaction = 1_024;

    private readonly IHotfixRuntimeAccessor? runtimeAccessor;
    private readonly TimeProvider timeProvider;
    private readonly LakonaTimerOptions options;
    private readonly ILakonaTimerSchedulerObserver observer;
    private readonly ILogger<LakonaTimerScheduler> logger;
    private readonly LakonaTimerCallbackResolver callbackResolver;
    private readonly LakonaTimerArgsSerializer argsSerializer;
    private readonly LakonaTimerDiagnostics diagnostics;
    private readonly object gate = new();
    private readonly object lifecycleGate = new();
    private readonly Dictionary<TimerId, LakonaTimerRegistration> registrations = [];
    private readonly PriorityQueue<LakonaTimerHeapEntry, long> heap = new();
    private readonly SemaphoreSlim wakeSignal = new(0);
    private readonly Channel<LakonaTimerDispatchWorkItem> dispatches;
    private readonly CancellationTokenSource stopping = new();
    private Task? dispatchTask;
    private readonly HashSet<Task> actorDispatches = [];
    private ILakonaTimerBackend? timerBackend;
    private Task? loopTask;
    private Task? stopTask;
    private bool started;
    private bool disposed;
    private int staleHeapEntryCount;

    public LakonaTimerScheduler(
        IHotfixRuntimeAccessor? runtimeAccessor,
        TimeProvider timeProvider,
        LakonaTimerOptions options,
        ILakonaTimerSchedulerObserver? observer,
        ILogger<LakonaTimerScheduler> logger)
        : this(
            runtimeAccessor,
            timeProvider,
            options,
            observer,
            logger,
            new LakonaTimerCallbackResolver(),
            new LakonaTimerArgsSerializer())
    {
    }

    internal LakonaTimerScheduler(
        IHotfixRuntimeAccessor? runtimeAccessor,
        TimeProvider timeProvider,
        LakonaTimerOptions options,
        ILakonaTimerSchedulerObserver? observer,
        ILogger<LakonaTimerScheduler> logger,
        LakonaTimerCallbackResolver callbackResolver,
        LakonaTimerArgsSerializer argsSerializer)
    {
        this.runtimeAccessor = runtimeAccessor;
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.options.Validate();
        this.observer = observer ?? NullLakonaTimerSchedulerObserver.Instance;
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.callbackResolver = callbackResolver ?? throw new ArgumentNullException(nameof(callbackResolver));
        this.argsSerializer = argsSerializer ?? throw new ArgumentNullException(nameof(argsSerializer));
        diagnostics = new LakonaTimerDiagnostics(ObservePopulation);
        dispatches = Channel.CreateBounded<LakonaTimerDispatchWorkItem>(
            new BoundedChannelOptions(this.options.DispatchQueueCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true
            });
    }

    internal int LoopCount { get; private set; }

    internal DateTimeOffset GetUtcNow()
    {
        return timeProvider.GetUtcNow();
    }

    internal static bool ShouldCorrectArmingDrift(TimeSpan requestedDelay, TimeSpan remainingDelay)
    {
        return requestedDelay - remainingDelay > DelayArmingDriftTolerance;
    }

    internal void AttachBackend(ILakonaTimerBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        timerBackend = backend;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (started)
            {
                return Task.CompletedTask;
            }

            started = true;
            LoopCount++;
            loopTask = RunLoopAsync(stopping.Token);
            dispatchTask = RunDispatchLoopAsync(stopping.Token);
        }

        Signal();
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task currentStopTask;
        lock (lifecycleGate)
        {
            if (!started)
            {
                return;
            }

            stopTask ??= StopCoreAsync();
            currentStopTask = stopTask;
        }

        await currentStopTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task StopCoreAsync()
    {
        CancelSchedulerStop();
        Signal();
        dispatches.Writer.TryComplete();
        Task[] tasks = [loopTask!, dispatchTask!];
        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
            Task[] active;
            lock (gate) active = actorDispatches.ToArray();
            await Task.WhenAll(active).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
        }
        finally
        {
            ClearRegistrations();
        }
    }

    private void ClearRegistrations()
    {
        lock (gate)
        {
            foreach (var registration in registrations.Values)
            {
                registration.Destroy();
                registration.OwnerCancellation.Unregister();
            }
            registrations.Clear();
            heap.Clear();
            staleHeapEntryCount = 0;
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (lifecycleGate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
        }

        try
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            ClearRegistrations();
            diagnostics.Dispose();
            stopping.Dispose();
            wakeSignal.Dispose();
        }
    }

    public void Dispose()
    {
        lock (lifecycleGate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
        }

        try
        {
            StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        finally
        {
            ClearRegistrations();
            diagnostics.Dispose();
            stopping.Dispose();
            wakeSignal.Dispose();
        }
    }

    internal IReadOnlyCollection<LakonaTimerDescriptor> Descriptors
    {
        get
        {
            lock (gate)
            {
                return registrations.Values
                    .Where(static registration => !registration.Destroyed)
                    .Select(static registration => registration.Descriptor)
                    .ToArray();
            }
        }
    }

    internal void Add(LakonaTimerDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(descriptor.Owner);
        descriptor.Owner.Stopping.ThrowIfCancellationRequested();
        stopping.Token.ThrowIfCancellationRequested();
        var registration = new LakonaTimerRegistration(descriptor);
        try
        {
            var capacityExceeded = false;
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                stopping.Token.ThrowIfCancellationRequested();
                if (!registrations.ContainsKey(descriptor.TimerId)
                    && registrations.Count >= options.MaxActiveTimers)
                {
                    capacityExceeded = true;
                }
                else
                {
                    if (registrations.TryGetValue(descriptor.TimerId, out var replaced))
                    {
                        MarkScheduledEntryStale(replaced);
                        replaced.Destroy();
                    }

                    registration.NextDueTimestamp = GetDueTimestamp(descriptor.NextDueAtUtc);
                    registrations[descriptor.TimerId] = registration;
                    EnqueueHeap(registration);
                    CompactHeapIfNeeded();
                }
            }

            if (capacityExceeded)
            {
                diagnostics.RecordCapacityRejection();
                throw new InvalidOperationException(
                    $"Lakona timer scheduler reached its maximum active timer capacity of {options.MaxActiveTimers}.");
            }

            if (descriptor.Owner is { } owner)
            {
                var cancellation = owner.Stopping.UnsafeRegister(_ => Destroy(descriptor.TimerId), null);
                lock (gate)
                {
                    if (!registration.Destroyed) registration.OwnerCancellation = cancellation;
                    else cancellation.Unregister();
                }
            }
            Signal();
        }
        catch
        {
            RollbackAddedRegistration(registration);
            throw;
        }
    }

    internal void Destroy(TimerId timerId, Actors.ActorTimerOwner? owner = null)
    {
        LakonaTimerRegistration? registration;
        CancellationTokenSource? dispatchCancellation;
        lock (gate)
        {
            if (!registrations.TryGetValue(timerId, out registration)) return;
            if (owner is not null && !ReferenceEquals(registration.Descriptor.Owner, owner))
                throw new InvalidOperationException("The timer belongs to another Actor activation.");
            if (registration.Destroyed) return;

            MarkScheduledEntryStale(registration);
            registration.Destroy();
            registration.OwnerCancellation.Unregister();
            if (!registration.Pending) registrations.Remove(timerId);
            dispatchCancellation = registration.TakeDispatchCancellation();
            CompactHeapIfNeeded();
        }

        CancelDispatch(timerId, dispatchCancellation);
        Signal();
    }

    internal bool Contains(TimerId timerId)
    {
        lock (gate)
        {
            return registrations.TryGetValue(timerId, out var registration) && !registration.Destroyed;
        }
    }

    internal bool TryGetDescriptor(TimerId timerId, out LakonaTimerDescriptor descriptor)
    {
        lock (gate)
        {
            if (registrations.TryGetValue(timerId, out var registration) && !registration.Destroyed)
            {
                descriptor = registration.Descriptor;
                return true;
            }
        }

        descriptor = null!;
        return false;
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await ProcessDueTimersAsync(cancellationToken).ConfigureAwait(false);
                var delay = GetDelayUntilNextDue();
                if (delay is null)
                {
                    await wakeSignal.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                else if (delay == TimeSpan.Zero)
                {
                    await Task.Yield();
                }
                else
                {
                    await WaitForNextDueOrSignalAsync(delay.Value, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lakona timer scheduler loop stopped unexpectedly.");
            throw;
        }
    }

    private async Task WaitForNextDueOrSignalAsync(TimeSpan requestedDelay, CancellationToken cancellationToken)
    {
        while (true)
        {
            using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            // Task.Delay limits each wait to about 49 days. Keep the original deadline
            // in the heap and wait in bounded segments so long timers cannot stop the loop.
            var delayTask = Task.Delay(requestedDelay > MaximumDelay ? MaximumDelay : requestedDelay,
                timeProvider, waitCancellation.Token);
            var wakeTask = wakeSignal.WaitAsync(waitCancellation.Token);
            var remainingDelay = GetDelayUntilNextDue();
            if (wakeTask.IsCompletedSuccessfully)
            {
                await waitCancellation.CancelAsync().ConfigureAwait(false);
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (remainingDelay is null || remainingDelay == TimeSpan.Zero)
            {
                await waitCancellation.CancelAsync().ConfigureAwait(false);
                return;
            }

            if (!ShouldCorrectArmingDrift(requestedDelay, remainingDelay.Value))
            {
                await Task.WhenAny(delayTask, wakeTask).ConfigureAwait(false);
                await waitCancellation.CancelAsync().ConfigureAwait(false);
                return;
            }

            await waitCancellation.CancelAsync().ConfigureAwait(false);
            if (wakeTask.IsCompletedSuccessfully)
            {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            remainingDelay = GetDelayUntilNextDue();
            if (remainingDelay is null || remainingDelay == TimeSpan.Zero)
            {
                return;
            }

            requestedDelay = remainingDelay.Value;
            await Task.Yield();
        }
    }

    private async Task ProcessDueTimersAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            LakonaTimerDispatchObservation? queued = null;
            LakonaTimerDispatchObservation? full = null;
            LakonaTimerHeapObservation? stale = null;
            lock (gate)
            {
                if (!heap.TryPeek(out var entry, out var priority) || priority > timeProvider.GetTimestamp())
                    return;
                if (!registrations.TryGetValue(entry.TimerId, out var registration)
                    || registration.Destroyed || registration.Generation != entry.Generation)
                {
                    heap.Dequeue();
                    ConsumeStaleHeapEntry();
                    stale = new LakonaTimerHeapObservation(entry.TimerId, entry.Generation);
                }
                else
                {
                    var observedAt = timeProvider.GetUtcNow();
                    var work = new LakonaTimerDispatchWorkItem(registration.TimerId,
                        registration.DispatchGeneration + 1, registration.NextDueAtUtc, observedAt);
                    if (dispatches.Writer.TryWrite(work))
                    {
                        heap.Dequeue();
                        registration.DispatchGeneration++;
                        registration.Pending = true;
                        queued = CreateObservation(registration, observedAt);
                    }
                    else
                    {
                        // Keep the same due entry until capacity becomes available.
                        full = CreateObservation(registration, observedAt);
                    }
                }
            }

            if (stale is { } staleEntry)
                NotifyObserver(observer => observer.OnStaleHeapEntry(staleEntry), "stale heap entry");
            if (queued is { } observation)
                NotifyObserver(observer => observer.OnDispatchQueued(observation), "dispatch queued");
            if (full is { } deferred)
            {
                NotifyObserver(observer => observer.OnDispatchQueueFull(deferred), "dispatch queue full");
                if (!await dispatches.Writer.WaitToWriteAsync(cancellationToken).ConfigureAwait(false)) return;
            }
        }
    }

    private TimeSpan? GetDelayUntilNextDue()
    {
        List<LakonaTimerHeapObservation>? staleObservations = null;
        TimeSpan? delay;
        lock (gate)
        {
            while (heap.TryPeek(out var entry, out _)
                && (!registrations.TryGetValue(entry.TimerId, out var registration)
                    || registration.Destroyed
                    || registration.Generation != entry.Generation))
            {
                heap.Dequeue();
                ConsumeStaleHeapEntry();
                staleObservations ??= [];
                staleObservations.Add(new LakonaTimerHeapObservation(entry.TimerId, entry.Generation));
            }

            if (!heap.TryPeek(out _, out var priority))
            {
                delay = null;
            }
            else
            {
                delay = GetDelayUntilTimestamp(priority);
            }
        }

        if (staleObservations is not null)
        {
            foreach (var observation in staleObservations)
            {
                NotifyObserver(
                    observer => observer.OnStaleHeapEntry(observation),
                    "stale heap entry");
            }
        }

        return delay;
    }

    private async Task RunDispatchLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var workItem in dispatches.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                // Mailboxes own serialization; retained registrations bound pending dispatches.
                var task = DispatchAsync(workItem, cancellationToken);
                lock (gate) actorDispatches.Add(task);
                _ = ObserveActorDispatchAsync(task);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task ObserveActorDispatchAsync(Task task)
    {
        try { await task.ConfigureAwait(false); }
        finally { lock (gate) actorDispatches.Remove(task); }
    }

    private async Task DispatchAsync(LakonaTimerDispatchWorkItem workItem, CancellationToken shutdownToken)
    {
        LakonaTimerRegistration registration;
        CancellationTokenSource dispatchCancellation;
        lock (gate)
        {
            if (!registrations.TryGetValue(workItem.TimerId, out registration!)
                || registration.DispatchGeneration != workItem.DispatchGeneration)
            {
                return;
            }

            if (registration.Destroyed)
            {
                registrations.Remove(workItem.TimerId);
                return;
            }
            registration.StartedTimestamp = timeProvider.GetTimestamp();
            dispatchCancellation = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
            registration.DispatchCancellation = dispatchCancellation;
        }

        var observation = new LakonaTimerDispatchObservation(
            workItem.TimerId,
            workItem.DueAtUtc,
            workItem.ObservedAtUtc,
            registration.Period,
            workItem.DispatchGeneration);
        NotifyObserver(observer => observer.OnDispatchStarted(observation), "dispatch started");
        try
        {
            await registration.Descriptor.Owner!.InvokeAsync(
                (actor, ct) => ExecuteCallbackAsync(actor, ct), dispatchCancellation.Token).ConfigureAwait(false);

            async ValueTask ExecuteCallbackAsync(object actor, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lock (gate)
                {
                    if (registration.Destroyed) return;
                    registration.StartedTimestamp = timeProvider.GetTimestamp();
                }
                var accessor = runtimeAccessor
                    ?? throw new InvalidOperationException("Lakona timer dispatch requires a hotfix runtime accessor.");
                using var lease = accessor.AcquireCurrent();
                cancellationToken.ThrowIfCancellationRequested();
                using var dispatchScope = Dispatch.HotfixDispatchRuntimeScope.Enter(lease);
                var backend = timerBackend
                    ?? throw new InvalidOperationException("Lakona timer dispatch requires a timer backend.");
                using (LakonaTimerRuntime.Enter(backend, lease))
                    await InvokeCallbackAsync(lease.Snapshot, registration.Descriptor, workItem, cancellationToken, actor)
                        .ConfigureAwait(false);
            }

            NotifyObserver(observer => observer.OnDispatchCompleted(observation), "dispatch completed");
        }
        catch (OperationCanceledException) when (dispatchCancellation.IsCancellationRequested)
        {
            NotifyObserver(observer => observer.OnDispatchCompleted(observation), "dispatch canceled");
        }
        catch (Exception ex)
        {
            NotifyObserver(observer => observer.OnDispatchFailed(observation, ex), "dispatch failed");
            logger.LogWarning(ex, "Lakona timer {TimerId} callback failed.", workItem.TimerId);
        }
        finally
        {
            CompleteDispatch(workItem, registration);
            dispatchCancellation.Dispose();
        }
    }

    private async ValueTask InvokeCallbackAsync(
        HotfixRuntimeSnapshot snapshot,
        LakonaTimerDescriptor descriptor,
        LakonaTimerDispatchWorkItem workItem,
        CancellationToken cancellationToken,
        object actor)
    {
        var callback = callbackResolver.Resolve(snapshot, descriptor);
        var argsType = callback.ArgsType;
        var args = argsSerializer.Deserialize(descriptor.SerializerId, descriptor.JsonPayload, argsType);
        var constructedTick = Activator.CreateInstance(
            typeof(TimerTick<>).MakeGenericType(argsType),
            descriptor.TimerId,
            args,
            snapshot.Services,
            workItem.DueAtUtc,
            timeProvider.GetUtcNow(),
            cancellationToken);
        await snapshot.DispatchTable!
            .InvokeTimerAsync(descriptor.MethodId, constructedTick!, actor)
            .ConfigureAwait(false);
    }

    private void CompleteDispatch(LakonaTimerDispatchWorkItem workItem, LakonaTimerRegistration registration)
    {
        lock (gate)
        {
            registration.TakeDispatchCancellation();
            if (!registrations.TryGetValue(workItem.TimerId, out var current) || !ReferenceEquals(current, registration))
                return;
            registration.Pending = false;
            if (registration.Destroyed || registration.Period is null || stopping.IsCancellationRequested)
            {
                registration.Destroy();
                registration.OwnerCancellation.Unregister();
                registrations.Remove(workItem.TimerId);
                return;
            }

            registration.NextDueTimestamp = Math.Max(timeProvider.GetTimestamp(),
                AddTimestampDelta(registration.StartedTimestamp, GetTimestampDelta(registration.Period.Value)));
            var now = timeProvider.GetUtcNow();
            var nextDelay = GetDelayUntilTimestamp(registration.NextDueTimestamp);
            registration.NextDueAtUtc = nextDelay >= DateTimeOffset.MaxValue - now
                ? DateTimeOffset.MaxValue : now.Add(nextDelay);
            registration.Generation++;
            EnqueueHeap(registration);
            Signal();
        }
    }

    private void EnqueueHeap(LakonaTimerRegistration registration)
    {
        heap.Enqueue(
            new LakonaTimerHeapEntry(registration.TimerId, registration.Generation),
            registration.NextDueTimestamp);
    }

    private void MarkScheduledEntryStale(LakonaTimerRegistration registration)
    {
        if (!registration.Pending)
        {
            staleHeapEntryCount++;
        }
    }

    private void ConsumeStaleHeapEntry()
    {
        if (staleHeapEntryCount > 0)
        {
            staleHeapEntryCount--;
        }
    }

    private void CompactHeapIfNeeded()
    {
        if (staleHeapEntryCount < MinimumStaleHeapEntriesBeforeCompaction
            || staleHeapEntryCount <= registrations.Count)
        {
            return;
        }

        heap.Clear();
        foreach (var registration in registrations.Values)
        {
            if (!registration.Destroyed
                && !registration.Pending)
            {
                EnqueueHeap(registration);
            }
        }

        staleHeapEntryCount = 0;
    }

    private LakonaTimerPopulation ObservePopulation()
    {
        lock (gate)
        {
            return new LakonaTimerPopulation(
                registrations.Count,
                heap.Count,
                staleHeapEntryCount);
        }
    }

    private long GetDueTimestamp(DateTimeOffset dueAtUtc)
    {
        var delay = dueAtUtc - timeProvider.GetUtcNow();
        return AddTimestampDelta(timeProvider.GetTimestamp(), GetTimestampDelta(delay));
    }

    private TimeSpan GetDelayUntilTimestamp(long dueTimestamp)
    {
        var timestampDelta = dueTimestamp - timeProvider.GetTimestamp();
        if (timestampDelta <= 0)
        {
            return TimeSpan.Zero;
        }

        var ticks = decimal.Ceiling(
            (decimal)timestampDelta * TimeSpan.TicksPerSecond / timeProvider.TimestampFrequency);
        if (ticks >= TimeSpan.MaxValue.Ticks)
        {
            return TimeSpan.MaxValue;
        }

        return TimeSpan.FromTicks((long)ticks);
    }

    private long GetTimestampDelta(TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero)
        {
            return 0;
        }

        var timestampDelta = decimal.Ceiling(
            (decimal)delay.Ticks * timeProvider.TimestampFrequency / TimeSpan.TicksPerSecond);
        return timestampDelta >= long.MaxValue ? long.MaxValue : (long)timestampDelta;
    }

    private static long AddTimestampDelta(long timestamp, long delta)
    {
        if (delta <= 0)
        {
            return timestamp;
        }

        return long.MaxValue - timestamp < delta ? long.MaxValue : timestamp + delta;
    }

    private void CancelDispatch(TimerId timerId, CancellationTokenSource? dispatchCancellation)
    {
        if (dispatchCancellation is null)
        {
            return;
        }

        try
        {
            dispatchCancellation.Cancel();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Lakona timer {TimerId} cancellation callback failed.", timerId);
        }
    }

    private void RollbackAddedRegistration(LakonaTimerRegistration registration)
    {
        CancellationTokenSource? dispatchCancellation = null;
        lock (gate)
        {
            if (registrations.TryGetValue(registration.TimerId, out var current)
                && ReferenceEquals(current, registration))
            {
                registrations.Remove(registration.TimerId);
                MarkScheduledEntryStale(registration);
                registration.Destroy();
                dispatchCancellation = registration.TakeDispatchCancellation();
                CompactHeapIfNeeded();
            }
        }

        CancelDispatch(registration.TimerId, dispatchCancellation);
    }

    private void CancelSchedulerStop()
    {
        try
        {
            stopping.Cancel();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Lakona timer scheduler shutdown cancellation callback failed.");
        }
    }

    private LakonaTimerDispatchObservation CreateObservation(
        LakonaTimerRegistration registration,
        DateTimeOffset observedAtUtc)
    {
        return new LakonaTimerDispatchObservation(
            registration.TimerId,
            registration.NextDueAtUtc,
            observedAtUtc,
            registration.Period,
            registration.Generation);
    }

    private void Signal()
    {
        try
        {
            wakeSignal.Release();
        }
        catch (SemaphoreFullException)
        {
        }
        catch (ObjectDisposedException) when (disposed)
        {
        }
    }

    private void NotifyObserver(
        Action<ILakonaTimerSchedulerObserver> notify,
        string eventName)
    {
        try
        {
            notify(observer);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Lakona timer scheduler observer failed for {ObserverEvent}.", eventName);
        }
    }

    private readonly record struct LakonaTimerDispatchWorkItem(
        TimerId TimerId,
        long DispatchGeneration,
        DateTimeOffset DueAtUtc,
        DateTimeOffset ObservedAtUtc);
}
