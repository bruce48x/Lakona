using System.Threading.Channels;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Hotfix.Timers;

internal sealed class LakonaTimerScheduler : IHostedService, IAsyncDisposable, IDisposable
{
    private readonly IHotfixRuntimeAccessor? runtimeAccessor;
    private readonly TimeProvider timeProvider;
    private readonly LakonaTimerOptions options;
    private readonly ILakonaTimerSchedulerObserver observer;
    private readonly ILogger<LakonaTimerScheduler> logger;
    private readonly LakonaTimerCallbackResolver callbackResolver;
    private readonly LakonaTimerArgsSerializer argsSerializer;
    private readonly object gate = new();
    private readonly Dictionary<TimerId, LakonaTimerRegistration> registrations = [];
    private readonly PriorityQueue<LakonaTimerHeapEntry, long> heap = new();
    private readonly SemaphoreSlim wakeSignal = new(0);
    private readonly Channel<LakonaTimerDispatchWorkItem> dispatches;
    private readonly CancellationTokenSource stopping = new();
    private readonly List<Task> workers = [];
    private Task? loopTask;
    private bool started;

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

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
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

        Signal();
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!started)
        {
            return;
        }

        stopping.Cancel();
        Signal();
        dispatches.Writer.TryComplete();
        Task[] tasks = loopTask is null ? workers.ToArray() : workers.Append(loopTask).ToArray();
        try
        {
            await Task.WhenAll(tasks).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
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
        lock (gate)
        {
            registrations[descriptor.TimerId] = registration;
            EnqueueHeap(registration);
        }

        Signal();
    }

    internal void Destroy(TimerId timerId)
    {
        LakonaTimerRegistration? registration;
        lock (gate)
        {
            if (!registrations.Remove(timerId, out registration))
            {
                return;
            }

            registration.Destroy();
        }

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
                    using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    var delayTask = Task.Delay(delay.Value, timeProvider, waitCancellation.Token);
                    var wakeTask = wakeSignal.WaitAsync(waitCancellation.Token);
                    await Task.WhenAny(delayTask, wakeTask).ConfigureAwait(false);
                    await waitCancellation.CancelAsync().ConfigureAwait(false);
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

                var nowTicks = timeProvider.GetUtcNow().UtcTicks;
                if (priority > nowTicks)
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
                    var observation = CreateObservation(registration, observedAtUtc);
                    if (registration.Pending)
                    {
                        skippedObservation = observation;
                        ReschedulePeriodicDueSlot(registration, observedAtUtc);
                    }
                    else
                    {
                        registration.Pending = true;
                        workItem = new LakonaTimerDispatchWorkItem(
                            registration.TimerId,
                            registration.Generation,
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
                                ReschedulePeriodicDueSlot(registration, observedAtUtc);
                            }
                        }
                        else if (registration.Period is not null)
                        {
                            registration.NextDueAtUtc = registration.NextDueAtUtc.Add(registration.Period.Value);
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
        lock (gate)
        {
            while (heap.TryPeek(out var entry, out _)
                && (!registrations.TryGetValue(entry.TimerId, out var registration)
                    || registration.Destroyed
                    || registration.Generation != entry.Generation))
            {
                heap.Dequeue();
                NotifyObserver(
                    observer => observer.OnStaleHeapEntry(new LakonaTimerHeapObservation(entry.TimerId, entry.Generation)),
                    "stale heap entry");
            }

            if (!heap.TryPeek(out _, out var priority))
            {
                return null;
            }

            var ticks = priority - timeProvider.GetUtcNow().UtcTicks;
            return ticks <= 0 ? TimeSpan.Zero : TimeSpan.FromTicks(ticks);
        }
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
                || registration.Generation != workItem.Generation)
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
            workItem.Generation);
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
                    || current.Generation != workItem.Generation
                    || dispatchCancellation.IsCancellationRequested)
                {
                    return;
                }
            }

            await InvokeCallbackAsync(lease.Snapshot, registration.Descriptor, workItem, dispatchCancellation.Token)
                .ConfigureAwait(false);
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
        var method = callbackResolver.Resolve(snapshot, descriptor);
        var tickType = method.GetParameters()[0].ParameterType;
        var argsType = tickType.GetGenericArguments()[0];
        var args = argsSerializer.Deserialize(descriptor.SerializerId, descriptor.JsonPayload, argsType);
        var constructedTick = Activator.CreateInstance(
            typeof(TimerTick<>).MakeGenericType(argsType),
            descriptor.TimerId,
            args,
            snapshot.Services,
            workItem.DueAtUtc,
            timeProvider.GetUtcNow(),
            cancellationToken);
        var result = method.Invoke(null, [constructedTick]);
        await ((ValueTask)result!).ConfigureAwait(false);
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

            if (registration.Generation == workItem.Generation)
            {
                ReschedulePeriodicDueSlot(registration, timeProvider.GetUtcNow());
                Signal();
            }
        }
    }

    private void ReschedulePeriodicDueSlot(LakonaTimerRegistration registration, DateTimeOffset observedAtUtc)
    {
        if (registration.Period is null || registration.Destroyed)
        {
            return;
        }

        registration.NextDueAtUtc = observedAtUtc.Add(registration.Period.Value);
        registration.Generation++;
        EnqueueHeap(registration);
    }

    private void EnqueueHeap(LakonaTimerRegistration registration)
    {
        heap.Enqueue(
            new LakonaTimerHeapEntry(registration.TimerId, registration.Generation),
            registration.NextDueAtUtc.UtcTicks);
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
        long Generation,
        DateTimeOffset DueAtUtc,
        DateTimeOffset ObservedAtUtc);
}
