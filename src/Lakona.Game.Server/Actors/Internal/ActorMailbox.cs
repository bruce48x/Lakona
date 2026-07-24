using System.Diagnostics;
using System.Threading.Channels;

namespace Lakona.Game.Server.Actors.Internal;

internal sealed class ActorMailbox
{
    private static long _totalQueuedCount;

    private readonly ActorId _actorId;
    private readonly Type _actorType;
    private readonly Channel<ActorMailboxEntry> _channel;
    private readonly SemaphoreSlim _availableSlots;
    private readonly Func<ActorWorkItem, ValueTask<object?>> _dispatch;
    private readonly Func<ActorCallContext?> _getCurrentCallContext;
    private readonly Action<ActorCallContext?> _setCurrentCallContext;
    private readonly ActorRuntimeDiagnosticsPublisher _diagnostics;
    private readonly TimeSpan? _slowMessageThreshold;
    private readonly Task _completion;
    private readonly object _stopGate = new();
    private Task? _stopTask;
    private int _stopping;
    private long _queuedCount;
    private long _enqueuedCount;
    private long _processedCount;
    private long _rejectedCount;

    internal ActorMailbox(
        ActorId actorId,
        Type actorType,
        int capacity,
        TimeSpan? slowMessageThreshold,
        Func<ActorWorkItem, ValueTask<object?>> dispatch,
        Func<ActorCallContext?> getCurrentCallContext,
        Action<ActorCallContext?> setCurrentCallContext,
        ActorRuntimeDiagnosticsPublisher diagnostics)
    {
        _actorId = actorId;
        _actorType = actorType;
        _dispatch = dispatch;
        _getCurrentCallContext = getCurrentCallContext;
        _setCurrentCallContext = setCurrentCallContext;
        _diagnostics = diagnostics;
        _slowMessageThreshold = slowMessageThreshold;
        Capacity = capacity;
        _availableSlots = new SemaphoreSlim(capacity, capacity);
        _channel = Channel.CreateBounded<ActorMailboxEntry>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        _completion = ProcessAsync();
    }

    internal int Capacity { get; }

    internal Task Completion => _completion;

    internal bool IsStopping => Volatile.Read(ref _stopping) != 0;

    internal ActorState State => !IsStopping
        ? ActorState.Active
        : Completion.IsCompleted
            ? ActorState.Dead
            : ActorState.Draining;

    internal ActorTellResult TryPost(ActorWorkItem work)
    {
        if (IsStopping)
        {
            Reject(work, "stopping", "Actor is stopping.");
            return ActorTellResult.ActorUnavailable;
        }

        ActorMailboxEntry entry = CreateEntry(work, response: null);
        if (TryWrite(entry, allowStopping: false))
        {
            return ActorTellResult.Accepted;
        }

        bool completed = Completion.IsCompleted || IsStopping;
        Reject(
            work,
            completed ? "completed" : "full",
            completed ? "Actor mailbox is completed." : "Actor mailbox is full.");
        return completed ? ActorTellResult.ActorUnavailable : ActorTellResult.MailboxFull;
    }

    internal async ValueTask<object?> CallAsync(
        ActorWorkItem work,
        TimeSpan queueTimeout,
        TimeSpan responseTimeout,
        CancellationToken cancellationToken,
        bool allowStopping = false)
    {
        if (queueTimeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(queueTimeout));
        }

        if (responseTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(responseTimeout));
        }

        if (IsStopping && !allowStopping)
        {
            Reject(work, "stopping", "Actor is stopping.");
            throw new InvalidOperationException($"Actor {_actorId} is stopping.");
        }

        ActorCallContext? caller = GetActiveCallContext();
        IReadOnlyList<ActorId> callChain = caller?.CallChain ?? Array.Empty<ActorId>();
        if (callChain.Contains(_actorId))
        {
            throw new InvalidOperationException(
                $"Circular actor call detected. The target actor {_actorId.Value} is already in the call chain " +
                $"({string.Join(" -> ", callChain.Select(static id => id.Value))}). " +
                "Restructure the actors to avoid circular dependencies.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        LakonaActorDiagnostics.CallStartedCounter.Add(1);

        long startedAt = Stopwatch.GetTimestamp();
        TaskCompletionSource<object?> response = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ActorMailboxEntry entry = CreateEntry(work, response, callChain);

        using CancellationTokenRegistration cancellationRegistration = cancellationToken.Register(
            static state => ((TaskCompletionSource<object?>)state!).TrySetCanceled(),
            response);

        await QueueCallAsync(
            work,
            entry,
            queueTimeout,
            responseTimeout,
            cancellationToken,
            caller,
            callChain,
            startedAt,
            allowStopping).ConfigureAwait(false);

        return await WaitForResponseAsync(
            work,
            response,
            queueTimeout,
            responseTimeout,
            cancellationToken,
            caller,
            callChain,
            startedAt).ConfigureAwait(false);
    }

    internal ActorMailboxMetrics GetMetrics()
    {
        return new ActorMailboxMetrics(
            Capacity,
            checked((int)Volatile.Read(ref _queuedCount)),
            Interlocked.Read(ref _enqueuedCount),
            Interlocked.Read(ref _processedCount),
            Interlocked.Read(ref _rejectedCount),
            Completion.IsCompleted);
    }

    internal Task RequestStopAsync()
    {
        lock (_stopGate)
        {
            if (_stopTask is not null)
            {
                return _stopTask;
            }

            BeginStopping();
            _channel.Writer.TryComplete();
            _stopTask = Completion;
            return _stopTask;
        }
    }

    internal void BeginStopping()
    {
        Interlocked.Exchange(ref _stopping, 1);
    }

    internal void CancelStopping()
    {
        lock (_stopGate)
        {
            if (_stopTask is null)
            {
                Interlocked.Exchange(ref _stopping, 0);
            }
        }
    }

    internal async ValueTask<ActorMailboxStopResult> StopAsync(TimeSpan timeout)
    {
        Task stopTask = RequestStopAsync();
        using CancellationTokenSource timeoutCts = new(timeout);

        try
        {
            await stopTask.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            return ActorMailboxStopResult.Stopped;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            return ActorMailboxStopResult.TimedOut;
        }
    }

    internal static long GetTotalQueuedCount()
    {
        return Volatile.Read(ref _totalQueuedCount);
    }

    private ActorMailboxEntry CreateEntry(
        ActorWorkItem work,
        TaskCompletionSource<object?>? response,
        IReadOnlyList<ActorId>? callChain = null)
    {
        return new ActorMailboxEntry(
            work,
            response,
            callChain ?? GetActiveCallContext()?.CallChain ?? Array.Empty<ActorId>(),
            Activity.Current?.Context ?? default);
    }

    private async ValueTask QueueCallAsync(
        ActorWorkItem work,
        ActorMailboxEntry entry,
        TimeSpan queueTimeout,
        TimeSpan responseTimeout,
        CancellationToken cancellationToken,
        ActorCallContext? caller,
        IReadOnlyList<ActorId> callChain,
        long startedAt,
        bool allowStopping)
    {
        if (queueTimeout == TimeSpan.Zero)
        {
            if (TryWrite(entry, allowStopping))
            {
                return;
            }

            if (Completion.IsCompleted || IsStopping && !allowStopping)
            {
                bool stopping = IsStopping && !Completion.IsCompleted;
                string reason = stopping ? "Actor is stopping." : "Actor mailbox is completed.";
                Reject(work, stopping ? "stopping" : "completed", reason);
                throw new InvalidOperationException($"Actor {_actorId} is unavailable: {reason}");
            }

            TimeoutException exception = PublishTimeout(
                caller,
                work,
                queueTimeout,
                responseTimeout,
                Stopwatch.GetElapsedTime(startedAt),
                ActorCallTimeoutReason.QueueTimeout,
                callChain,
                "The actor call timed out before it could be queued.");
            entry.Response!.TrySetException(exception);
            throw exception;
        }

        using CancellationTokenSource queueTimeoutCts = new(queueTimeout);
        using CancellationTokenSource linkedQueueCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            queueTimeoutCts.Token);

        try
        {
            await WriteAsync(entry, linkedQueueCts.Token, allowStopping).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            bool stopping = IsStopping && !Completion.IsCompleted;
            Reject(
                work,
                stopping ? "stopping" : "completed",
                stopping ? "Actor is stopping." : "Actor mailbox is completed.");
            throw;
        }
        catch (OperationCanceledException) when (
            queueTimeoutCts.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
            TimeoutException exception = PublishTimeout(
                caller,
                work,
                queueTimeout,
                responseTimeout,
                Stopwatch.GetElapsedTime(startedAt),
                ActorCallTimeoutReason.QueueTimeout,
                callChain,
                "The actor call timed out before it could be queued.");
            entry.Response!.TrySetException(exception);
            throw exception;
        }
    }

    private async ValueTask<object?> WaitForResponseAsync(
        ActorWorkItem work,
        TaskCompletionSource<object?> response,
        TimeSpan queueTimeout,
        TimeSpan responseTimeout,
        CancellationToken cancellationToken,
        ActorCallContext? caller,
        IReadOnlyList<ActorId> callChain,
        long startedAt)
    {
        using CancellationTokenSource responseTimeoutCts = new(responseTimeout);
        using CancellationTokenSource linkedResponseCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            responseTimeoutCts.Token);

        try
        {
            return await response.Task.WaitAsync(linkedResponseCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            responseTimeoutCts.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
            TimeoutException exception = PublishTimeout(
                caller,
                work,
                queueTimeout,
                responseTimeout,
                Stopwatch.GetElapsedTime(startedAt),
                ActorCallTimeoutReason.ResponseTimeout,
                callChain,
                "The actor call timed out.");
            response.TrySetException(exception);
            throw exception;
        }
    }

    private TimeoutException PublishTimeout(
        ActorCallContext? caller,
        ActorWorkItem work,
        TimeSpan queueTimeout,
        TimeSpan responseTimeout,
        TimeSpan elapsed,
        ActorCallTimeoutReason reason,
        IReadOnlyList<ActorId> callChain,
        string message)
    {
        return _diagnostics.PublishCallTimeout(
            caller?.ActorId,
            _actorId,
            work,
            queueTimeout,
            responseTimeout,
            elapsed,
            reason,
            callChain,
            message);
    }

    private async ValueTask WriteAsync(
        ActorMailboxEntry entry,
        CancellationToken cancellationToken,
        bool allowStopping)
    {
        await _availableSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (IsStopping && !allowStopping)
        {
            _availableSlots.Release();
            throw new InvalidOperationException("The actor mailbox is stopping.");
        }

        IncrementQueued();
        if (_channel.Writer.TryWrite(entry))
        {
            RecordAccepted(entry);
            return;
        }

        DecrementQueued();
        _availableSlots.Release();
        throw new InvalidOperationException("The actor mailbox is completed.");
    }

    private bool TryWrite(ActorMailboxEntry entry, bool allowStopping)
    {
        if (!_availableSlots.Wait(0))
        {
            return false;
        }

        if (IsStopping && !allowStopping)
        {
            _availableSlots.Release();
            return false;
        }

        IncrementQueued();
        if (_channel.Writer.TryWrite(entry))
        {
            RecordAccepted(entry);
            return true;
        }

        DecrementQueued();
        _availableSlots.Release();
        return false;
    }

    private void RecordAccepted(ActorMailboxEntry entry)
    {
        Interlocked.Increment(ref _enqueuedCount);
        LakonaActorDiagnostics.MessageAcceptedCounter.Add(1, CreateKindTag(entry));
    }

    private async Task ProcessAsync()
    {
        try
        {
            while (await _channel.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                while (_channel.Reader.TryRead(out ActorMailboxEntry? entry))
                {
                    DecrementQueued();
                    try
                    {
                        await DispatchAsync(entry).ConfigureAwait(false);
                    }
                    finally
                    {
                        Interlocked.Increment(ref _processedCount);
                        _availableSlots.Release();
                    }
                }
            }
        }
        catch (Exception exception)
        {
            _channel.Writer.TryComplete(exception);
            while (_channel.Reader.TryRead(out _))
            {
                DecrementQueued();
                _availableSlots.Release();
            }

            throw;
        }
    }

    private async ValueTask DispatchAsync(ActorMailboxEntry entry)
    {
        ActorCallContext? previousCallContext = GetActiveCallContext();
        IReadOnlyList<ActorId> callChain = AppendCallChain(entry.CallChain, _actorId);
        ActorCallContext currentCallContext = new(_actorId, callChain);
        long startedAt = _slowMessageThreshold is null ? 0 : Stopwatch.GetTimestamp();

        using Activity? activity = StartDispatchActivity(entry);
        activity?.SetTag("lakona-game.actor.type", _actorType.FullName ?? _actorType.Name);
        activity?.SetTag("lakona-game.actor.message.type", entry.Work.MessageType);
        activity?.SetTag("lakona-game.actor.message.kind", entry.Response is null ? "send" : "call");

        Exception? error = null;
        object? result = null;

        try
        {
            _setCurrentCallContext(currentCallContext);
            result = await _dispatch(entry.Work).ConfigureAwait(false);
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception exception)
        {
            error = exception;
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.SetTag("exception.type", exception.GetType().FullName);
        }
        finally
        {
            currentCallContext.Deactivate();
            _setCurrentCallContext(previousCallContext);
            LakonaActorDiagnostics.MessageProcessedCounter.Add(1, CreateKindTag(entry));

            if (_slowMessageThreshold is { } threshold)
            {
                TimeSpan elapsed = Stopwatch.GetElapsedTime(startedAt);
                if (elapsed >= threshold)
                {
                    activity?.AddEvent(new ActivityEvent(
                        "Lakona.Game.Actor.SlowMessage",
                        tags: new ActivityTagsCollection
                        {
                            ["lakona-game.actor.slow_message.elapsed_ms"] = elapsed.TotalMilliseconds
                        }));
                    activity?.SetTag("lakona-game.actor.slow_message", true);
                    activity?.SetTag("lakona-game.actor.slow_message.elapsed_ms", elapsed.TotalMilliseconds);
                    _diagnostics.PublishSlowMessage(_actorId, entry.Work, elapsed);
                }
            }

            if (error is null)
            {
                entry.Response?.TrySetResult(result);
            }
            else
            {
                entry.Response?.TrySetException(error);
            }
        }
    }

    private ActorCallContext? GetActiveCallContext()
    {
        ActorCallContext? context = _getCurrentCallContext();
        return context is { IsActive: true } ? context : null;
    }

    private void Reject(ActorWorkItem work, string metricReason, string reason)
    {
        Interlocked.Increment(ref _rejectedCount);
        LakonaActorDiagnostics.MessageRejectedCounter.Add(1, new KeyValuePair<string, object?>(
            "reason",
            metricReason));
        _diagnostics.PublishDeadLetter(_actorId, work, reason);
    }

    private void IncrementQueued()
    {
        Interlocked.Increment(ref _queuedCount);
        Interlocked.Increment(ref _totalQueuedCount);
    }

    private void DecrementQueued()
    {
        Interlocked.Decrement(ref _queuedCount);
        Interlocked.Decrement(ref _totalQueuedCount);
    }

    private static IReadOnlyList<ActorId> AppendCallChain(
        IReadOnlyList<ActorId> callChain,
        ActorId actorId)
    {
        ActorId[] next = new ActorId[callChain.Count + 1];
        for (int index = 0; index < callChain.Count; index++)
        {
            next[index] = callChain[index];
        }

        next[^1] = actorId;
        return next;
    }

    private static KeyValuePair<string, object?> CreateKindTag(ActorMailboxEntry entry)
    {
        return new KeyValuePair<string, object?>("kind", entry.Response is null ? "send" : "call");
    }

    private static Activity? StartDispatchActivity(ActorMailboxEntry entry)
    {
        if (entry.ParentActivityContext.TraceId != default)
        {
            return LakonaActorDiagnostics.ActivitySource.StartActivity(
                "Lakona.Actor.Actor.Dispatch",
                ActivityKind.Internal,
                entry.ParentActivityContext);
        }

        return LakonaActorDiagnostics.ActivitySource.StartActivity(
            "Lakona.Actor.Actor.Dispatch",
            ActivityKind.Internal);
    }
}
