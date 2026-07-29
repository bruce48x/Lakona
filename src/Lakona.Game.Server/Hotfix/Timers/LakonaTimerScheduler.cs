using System.Threading.Channels;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Hotfix.Timers;

internal sealed class LakonaTimerScheduler : IHostedService, IAsyncDisposable, IDisposable
{
    // Re-arming for ordinary sub-millisecond timer-construction drift creates churn without
    // materially improving due-time accuracy. Larger drift is corrected against the absolute due time.
    private static readonly TimeSpan DelayArmingDriftTolerance = TimeSpan.FromMilliseconds(1);

    private readonly IHotfixRuntimeAccessor? runtimeAccessor;
    private readonly TimeProvider timeProvider;
    private readonly LakonaTimerOptions options;
    private readonly ILakonaTimerSchedulerObserver observer;
    private readonly ILogger<LakonaTimerScheduler> logger;
    private readonly LakonaTimerCallbackResolver callbackResolver;
    private readonly LakonaTimerArgsSerializer argsSerializer;
    private readonly object gate = new();
    private readonly object lifecycleGate = new();
    private readonly Dictionary<TimerId, LakonaTimerRegistration> registrations = [];
    private readonly PriorityQueue<LakonaTimerHeapEntry, long> heap = new();
    private readonly SemaphoreSlim wakeSignal = new(0);
    private readonly Channel<LakonaTimerDispatchWorkItem> dispatches;
    private readonly CancellationTokenSource stopping = new();
    private readonly List<Task> workers = [];
    private ILakonaTimerBackend? timerBackend;
    private Task? loopTask;
    private Task? stopTask;
    private bool started;
    private bool disposed;

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
        dispatches = Channel.CreateBounded<LakonaTimerDispatchWorkItem>(
            new BoundedChannelOptions(this.options.DispatchQueueCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
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
            for (var index = 0; index < options.MaxConcurrentCallbacks; index++)
            {
                workers.Add(RunWorkerAsync(stopping.Token));
            }
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
        Task[] tasks = loopTask is null ? workers.ToArray() : workers.Append(loopTask).ToArray();
        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
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
        var registration = new LakonaTimerRegistration(descriptor);
        try
        {
            lock (gate)
            {
                registration.NextDueTimestamp = GetDueTimestamp(descriptor.NextDueAtUtc);
                registrations[descriptor.TimerId] = registration;
                EnqueueHeap(registration);
            }

            Signal();
        }
        catch
        {
            RollbackAddedRegistration(registration);
            throw;
        }
    }

    internal void Destroy(TimerId timerId)
    {
        LakonaTimerRegistration? registration;
        CancellationTokenSource? dispatchCancellation;
        lock (gate)
        {
            if (!registrations.Remove(timerId, out registration))
            {
                return;
            }

            registration.Destroy();
            dispatchCancellation = registration.TakeDispatchCancellation();
        }

        CancelDispatch(timerId, dispatchCancellation);
        Signal();
    }

    internal bool Contains(TimerId timerId)
    {
        lock (gate)
        {
            return registrations.ContainsKey(timerId);
        }
    }

    internal bool TryGetDescriptor(TimerId timerId, out LakonaTimerDescriptor descriptor)
    {
        lock (gate)
        {
            if (registrations.TryGetValue(timerId, out var registration))
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
                ProcessDueTimers();
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
            var delayTask = Task.Delay(requestedDelay, timeProvider, waitCancellation.Token);
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

    private void ProcessDueTimers()
    {
        while (true)
        {
            LakonaTimerDispatchWorkItem? workItem = null;
            LakonaTimerDispatchObservation? queuedObservation = null;
            LakonaTimerDispatchObservation? skippedObservation = null;
            LakonaTimerDispatchObservation? queueFullObservation = null;
            LakonaTimerHeapObservation? staleObservation = null;
            lock (gate)
            {
                if (!heap.TryPeek(out var entry, out var priority))
                {
                    return;
                }

                var nowTimestamp = timeProvider.GetTimestamp();
                if (priority > nowTimestamp)
                {
                    return;
                }

                heap.Dequeue();
                if (!registrations.TryGetValue(entry.TimerId, out var registration)
                    || registration.Destroyed
                    || registration.Generation != entry.Generation)
                {
                    staleObservation = new LakonaTimerHeapObservation(entry.TimerId, entry.Generation);
                }
                else
                {
                    var observedAtUtc = timeProvider.GetUtcNow();
                    var observedTimestamp = nowTimestamp;
                    var observation = CreateObservation(registration, observedAtUtc);
                    if (registration.Pending)
                    {
                        skippedObservation = observation;
                        ReschedulePeriodicDueSlot(registration, observedAtUtc, observedTimestamp);
                    }
                    else
                    {
                        registration.Pending = true;
                        registration.DispatchGeneration++;
                        workItem = new LakonaTimerDispatchWorkItem(
                            registration.TimerId,
                            registration.DispatchGeneration,
                            registration.NextDueAtUtc,
                            observedAtUtc);
                        if (!dispatches.Writer.TryWrite(workItem.Value))
                        {
                            queueFullObservation = observation;
                            skippedObservation = observation;
                            registration.Pending = false;
                            if (registration.Period is null)
                            {
                                registration.Destroy();
                                registrations.Remove(registration.TimerId);
                            }
                            else
                            {
                                ReschedulePeriodicDueSlot(registration, observedAtUtc, observedTimestamp);
                            }
                        }
                        else if (registration.Period is not null)
                        {
                            registration.NextDueAtUtc = GetNextFutureDueAtUtc(
                                registration.NextDueAtUtc,
                                registration.Period.Value,
                                observedAtUtc);
                            registration.NextDueTimestamp = GetNextFutureDueTimestamp(
                                registration.NextDueTimestamp,
                                registration.Period.Value,
                                observedTimestamp);
                            registration.FollowUpScheduled = true;
                            EnqueueHeap(registration);
                            queuedObservation = observation;
                        }
                        else
                        {
                            queuedObservation = observation;
                        }
                    }
                }
            }

            if (staleObservation is { } stale)
            {
                NotifyObserver(observer => observer.OnStaleHeapEntry(stale), "stale heap entry");
                continue;
            }

            if (queueFullObservation is { } full)
            {
                NotifyObserver(observer => observer.OnDispatchQueueFull(full), "dispatch queue full");
            }

            if (skippedObservation is { } skipped)
            {
                NotifyObserver(observer => observer.OnDispatchSkipped(skipped), "dispatch skipped");
                continue;
            }

            if (queuedObservation is { } queued)
            {
                NotifyObserver(observer => observer.OnDispatchQueued(queued), "dispatch queued");
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

    private async Task RunWorkerAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var workItem in dispatches.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                await DispatchAsync(workItem, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task DispatchAsync(LakonaTimerDispatchWorkItem workItem, CancellationToken shutdownToken)
    {
        LakonaTimerRegistration registration;
        CancellationTokenSource dispatchCancellation;
        lock (gate)
        {
            if (!registrations.TryGetValue(workItem.TimerId, out registration!)
                || registration.Destroyed
                || registration.DispatchGeneration != workItem.DispatchGeneration)
            {
                return;
            }

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
            var accessor = runtimeAccessor
                ?? throw new InvalidOperationException("Lakona timer dispatch requires a hotfix runtime accessor.");
            using var lease = accessor.AcquireCurrent();
            lock (gate)
            {
                if (!registrations.TryGetValue(workItem.TimerId, out var current)
                    || !ReferenceEquals(current, registration)
                    || current.Destroyed
                    || current.DispatchGeneration != workItem.DispatchGeneration
                    || dispatchCancellation.IsCancellationRequested)
                {
                    return;
                }
            }

            var backend = timerBackend
                ?? throw new InvalidOperationException("Lakona timer dispatch requires a timer backend.");
            using (LakonaTimerRuntime.Enter(backend, lease))
            {
                await InvokeCallbackAsync(lease.Snapshot, registration.Descriptor, workItem, dispatchCancellation.Token)
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
        CancellationToken cancellationToken)
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
            .InvokeTimerAsync(descriptor.MethodId, constructedTick!)
            .ConfigureAwait(false);
    }

    private void CompleteDispatch(LakonaTimerDispatchWorkItem workItem, LakonaTimerRegistration registration)
    {
        lock (gate)
        {
            registration.DispatchCancellation = null;
            if (!registrations.TryGetValue(workItem.TimerId, out var current) || !ReferenceEquals(current, registration))
            {
                return;
            }

            registration.Pending = false;
            if (registration.Destroyed)
            {
                registrations.Remove(workItem.TimerId);
                return;
            }

            if (registration.Period is null)
            {
                registration.Destroy();
                registrations.Remove(workItem.TimerId);
                return;
            }

            if (registration.FollowUpScheduled)
            {
                registration.FollowUpScheduled = false;
                return;
            }

            if (registration.DispatchGeneration == workItem.DispatchGeneration)
            {
                ReschedulePeriodicDueSlot(registration, timeProvider.GetUtcNow(), timeProvider.GetTimestamp());
                Signal();
            }
        }
    }

    private void ReschedulePeriodicDueSlot(
        LakonaTimerRegistration registration,
        DateTimeOffset observedAtUtc,
        long observedTimestamp)
    {
        if (registration.Period is null || registration.Destroyed)
        {
            return;
        }

        registration.NextDueAtUtc = GetNextFutureDueAtUtc(
            registration.NextDueAtUtc,
            registration.Period.Value,
            observedAtUtc);
        registration.NextDueTimestamp = GetNextFutureDueTimestamp(
            registration.NextDueTimestamp,
            registration.Period.Value,
            observedTimestamp);
        registration.Generation++;
        EnqueueHeap(registration);
    }

    private static DateTimeOffset GetNextFutureDueAtUtc(
        DateTimeOffset currentDueAtUtc,
        TimeSpan period,
        DateTimeOffset observedAtUtc)
    {
        if (period <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(period), period, "Period must be greater than zero.");
        }

        if (currentDueAtUtc > observedAtUtc)
        {
            return currentDueAtUtc;
        }

        var missedSlots = ((observedAtUtc.UtcTicks - currentDueAtUtc.UtcTicks) / period.Ticks) + 1;
        return currentDueAtUtc.AddTicks(checked(missedSlots * period.Ticks));
    }

    private long GetNextFutureDueTimestamp(
        long currentDueTimestamp,
        TimeSpan period,
        long observedTimestamp)
    {
        var periodTimestampDelta = GetTimestampDelta(period);
        if (periodTimestampDelta <= 0)
        {
            periodTimestampDelta = 1;
        }

        if (currentDueTimestamp > observedTimestamp)
        {
            return currentDueTimestamp;
        }

        var missedSlots = ((observedTimestamp - currentDueTimestamp) / periodTimestampDelta) + 1;
        return AddTimestampDelta(currentDueTimestamp, checked(missedSlots * periodTimestampDelta));
    }

    private void EnqueueHeap(LakonaTimerRegistration registration)
    {
        heap.Enqueue(
            new LakonaTimerHeapEntry(registration.TimerId, registration.Generation),
            registration.NextDueTimestamp);
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
                registration.Destroy();
                dispatchCancellation = registration.TakeDispatchCancellation();
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
