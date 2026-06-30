using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Lakona.Game.Server.Diagnostics;
using K = Lakona.Game.Server.Internal.ActorKernel;

namespace Lakona.Game.Server.Actors;

public sealed class LakonaActorRuntime : IActorRuntime, IActorLifecycle, IDisposable, IAsyncDisposable
{
    private static readonly AsyncLocal<ActorCell?> CurrentCell = new();

    private readonly ConcurrentDictionary<ActorId, ActorCell> _actors = new();
    private readonly ConcurrentDictionary<K.ActorId, ActorId> _actorIds = new();
    private readonly IServiceProvider _services;
    private readonly ActorRuntimeOptions _options;
    private readonly IReadOnlyList<IActorDiagnosticsObserver> _diagnosticsObservers;
    private readonly K.ActorSystem _actorSystem;

    public LakonaActorRuntime(IServiceProvider services, ActorRuntimeOptions options)
        : this(services, options, null)
    {
    }

    public LakonaActorRuntime(
        IServiceProvider services,
        ActorRuntimeOptions options,
        IEnumerable<IActorDiagnosticsObserver>? diagnosticsObservers = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _diagnosticsObservers = diagnosticsObservers?.ToArray() ?? [];
        _actorSystem = new K.ActorSystem(new K.ActorSystemOptions
        {
            MailboxCapacity = Math.Max(1, options.MailboxCapacity),
            SlowMessageThreshold = options.SlowMessageThreshold,
            MessageInterceptor = options.MessageInterceptor is null
                ? null
                : new KernelMessageInterceptorAdapter(this, options.MessageInterceptor)
        });
        _actorSystem.DeadLetterPublished += OnDeadLetterPublished;
        _actorSystem.SlowMessageDetected += OnSlowMessageDetected;
        _actorSystem.CallTimedOut += OnCallTimedOut;
    }

    public async ValueTask<TActor> GetOrCreateAsync<TActor>(
        ActorId id,
        CancellationToken cancellationToken = default)
        where TActor : class, IActor
    {
        var cell = GetOrCreateCell<TActor>(id);
        await cell.EnsureActivatedAsync(cancellationToken).ConfigureAwait(false);
        return (TActor)cell.Actor;
    }

    public async ValueTask<ActorCreateLocalResult> CreateLocalAsync<TActor>(
        ActorId actorId,
        ActorCreateOptions? options = null,
        CancellationToken cancellationToken = default)
        where TActor : class, IActor
    {
        return await CreateLocalAsync(typeof(TActor), actorId, options, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ActorCreateLocalResult> CreateLocalAsync(
        Type actorType,
        ActorId actorId,
        ActorCreateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actorType);
        _ = options ?? ActorCreateOptions.Default;
        cancellationToken.ThrowIfCancellationRequested();

        if (_actors.TryGetValue(actorId, out var existing))
        {
            return IsCompatibleActorType(existing.ActorType, actorType)
                ? new ActorCreateLocalResult(ActorCreateLocalStatus.AlreadyExistsSameType, actorId, actorType)
                : new ActorCreateLocalResult(
                    ActorCreateLocalStatus.AlreadyExistsDifferentType,
                    actorId,
                    actorType,
                    $"Actor id '{actorId.Value}' is already bound to '{existing.ActorType.FullName}'.");
        }

        var cell = GetOrCreateCell(actorType, actorId);
        await cell.EnsureActivatedAsync(cancellationToken).ConfigureAwait(false);
        return new ActorCreateLocalResult(ActorCreateLocalStatus.Created, actorId, actorType);
    }

    public async ValueTask<ActorDestroyLocalResult> DestroyLocalAsync<TActor>(
        ActorId actorId,
        ActorDestroyOptions? options = null,
        CancellationToken cancellationToken = default)
        where TActor : class, IActor
    {
        options ??= new ActorDestroyOptions();
        cancellationToken.ThrowIfCancellationRequested();

        var actorType = typeof(TActor);
        if (!_actors.TryGetValue(actorId, out var cell))
        {
            return new ActorDestroyLocalResult(ActorDestroyLocalStatus.NotFound, actorId, actorType);
        }

        if (!IsCompatibleActorType(cell.ActorType, actorType))
        {
            return new ActorDestroyLocalResult(
                ActorDestroyLocalStatus.TypeMismatch,
                actorId,
                actorType,
                $"Actor id '{actorId.Value}' is bound to '{cell.ActorType.FullName}'.");
        }

        var outcome = await StopAsync(actorId, options.DrainTimeout).ConfigureAwait(false);
        return outcome == ActorStopOutcome.TimedOut
            ? new ActorDestroyLocalResult(ActorDestroyLocalStatus.TimedOut, actorId, actorType)
            : new ActorDestroyLocalResult(ActorDestroyLocalStatus.Destroyed, actorId, actorType);
    }

    public async ValueTask TellAsync<TActor>(
        ActorId id,
        Func<TActor, CancellationToken, ValueTask> message,
        CancellationToken cancellationToken = default)
        where TActor : class, IActor
    {
        ArgumentNullException.ThrowIfNull(message);

        var cell = GetRequiredCell(typeof(TActor), id, nameof(TellAsync));
        await cell.InvokeAsync(
            static async (actor, state, ct) =>
            {
                var callback = (Func<TActor, CancellationToken, ValueTask>)state;
                await callback((TActor)actor, ct).ConfigureAwait(false);
                return null;
            },
            message,
            cancellationToken).ConfigureAwait(false);
    }

    public ActorTellResult TryTell<TActor>(
        ActorId id,
        Func<TActor, CancellationToken, ValueTask> message,
        CancellationToken cancellationToken = default)
        where TActor : class, IActor
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!TryGetCell(typeof(TActor), id, out var cell))
        {
            return ActorTellResult.ActorNotFound;
        }

        return cell.TryInvoke(
            static async (actor, state, ct) =>
            {
                var callback = (Func<TActor, CancellationToken, ValueTask>)state;
                await callback((TActor)actor, ct).ConfigureAwait(false);
                return null;
            },
            message,
            cancellationToken);
    }

    public async ValueTask<TResult> AskAsync<TActor, TResult>(
        ActorId id,
        Func<TActor, CancellationToken, ValueTask<TResult>> message,
        CancellationToken cancellationToken = default)
        where TActor : class, IActor
    {
        ArgumentNullException.ThrowIfNull(message);

        var cell = GetRequiredCell(typeof(TActor), id, nameof(AskAsync));
        var result = await cell.InvokeAsync(
            static async (actor, state, ct) =>
            {
                var callback = (Func<TActor, CancellationToken, ValueTask<TResult>>)state;
                return await callback((TActor)actor, ct).ConfigureAwait(false);
            },
            message,
            cancellationToken).ConfigureAwait(false);

        return result is TResult typedResult
            ? typedResult
            : throw new InvalidOperationException($"Actor call returned an invalid result for '{typeof(TResult).FullName}'.");
    }

    public async ValueTask TellAsync(
        Type actorType,
        ActorId id,
        Func<IActor, CancellationToken, ValueTask> message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actorType);
        ArgumentNullException.ThrowIfNull(message);

        var cell = GetRequiredCell(actorType, id, nameof(TellAsync));
        await cell.InvokeAsync(
            static async (actor, state, ct) =>
            {
                var callback = (Func<IActor, CancellationToken, ValueTask>)state;
                await callback(actor, ct).ConfigureAwait(false);
                return null;
            },
            message,
            cancellationToken).ConfigureAwait(false);
    }

    public ActorTellResult TryTell(
        Type actorType,
        ActorId id,
        Func<IActor, CancellationToken, ValueTask> message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actorType);
        ArgumentNullException.ThrowIfNull(message);

        if (!TryGetCell(actorType, id, out var cell))
        {
            return ActorTellResult.ActorNotFound;
        }

        return cell.TryInvoke(
            static async (actor, state, ct) =>
            {
                var callback = (Func<IActor, CancellationToken, ValueTask>)state;
                await callback(actor, ct).ConfigureAwait(false);
                return null;
            },
            message,
            cancellationToken);
    }

    public IAsyncDisposable RegisterTimer<TActor>(
        ActorId id,
        TimeSpan dueTime,
        TimeSpan? period,
        Func<TActor, CancellationToken, ValueTask> callback)
        where TActor : class, IActor
    {
        ArgumentNullException.ThrowIfNull(callback);

        var cell = GetRequiredCell(typeof(TActor), id, nameof(RegisterTimer));
        var envelope = new ActorRuntimeEnvelope(
            static async (actor, state, ct) =>
            {
                var callback = (Func<TActor, CancellationToken, ValueTask>)state;
                await callback((TActor)actor, ct).ConfigureAwait(false);
                return null;
            },
            callback,
            CancellationToken.None);

        return cell.RegisterTimer(envelope, dueTime, period);
    }

    public bool TryGetMailboxMetrics(ActorId id, out ActorMailboxMetrics metrics)
    {
        if (_actors.TryGetValue(id, out var cell))
        {
            metrics = cell.GetMailboxMetrics();
            return true;
        }

        metrics = default;
        return false;
    }

    public ActorState GetState(ActorId id)
    {
        if (_actors.TryGetValue(id, out var cell))
        {
            return cell.GetState();
        }

        return ActorState.Dead;
    }

    public ActorRuntimeDiagnosticsSnapshot GetDiagnosticsSnapshot()
    {
        var actorTypes = _actors.Values
            .Select(static cell => new
            {
                cell.ActorType,
                State = cell.GetState(),
                Metrics = cell.GetMailboxMetrics()
            })
            .Where(static actor => actor.State == ActorState.Active)
            .GroupBy(static actor => actor.ActorType.FullName ?? actor.ActorType.Name)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .Select(static group =>
            {
                var activeCount = 0;
                var mailboxQueuedSum = 0;
                var mailboxQueuedMax = 0;
                long mailboxEnqueuedCount = 0;
                long mailboxEnqueuedMax = 0;
                long mailboxProcessedCount = 0;
                long mailboxProcessedMax = 0;
                long mailboxRejectedCount = 0;
                long mailboxRejectedMax = 0;

                foreach (var actor in group)
                {
                    activeCount++;
                    mailboxQueuedSum += actor.Metrics.QueuedCount;
                    mailboxQueuedMax = Math.Max(mailboxQueuedMax, actor.Metrics.QueuedCount);
                    mailboxEnqueuedCount += actor.Metrics.EnqueuedCount;
                    mailboxEnqueuedMax = Math.Max(mailboxEnqueuedMax, actor.Metrics.EnqueuedCount);
                    mailboxProcessedCount += actor.Metrics.ProcessedCount;
                    mailboxProcessedMax = Math.Max(mailboxProcessedMax, actor.Metrics.ProcessedCount);
                    mailboxRejectedCount += actor.Metrics.RejectedCount;
                    mailboxRejectedMax = Math.Max(mailboxRejectedMax, actor.Metrics.RejectedCount);
                }

                return new ActorTypeDiagnosticsSnapshot(
                    group.Key,
                    activeCount,
                    mailboxQueuedSum,
                    mailboxQueuedMax,
                    mailboxEnqueuedCount,
                    mailboxEnqueuedMax,
                    mailboxProcessedCount,
                    mailboxProcessedMax,
                    mailboxRejectedCount,
                    mailboxRejectedMax);
            })
            .ToArray();

        return new ActorRuntimeDiagnosticsSnapshot(actorTypes);
    }

    public IReadOnlyList<ActorId> GetActiveActorIds(Type actorType)
    {
        ArgumentNullException.ThrowIfNull(actorType);
        return _actors
            .Where(pair => actorType.IsAssignableFrom(pair.Value.ActorType) && pair.Value.GetState() == ActorState.Active)
            .Select(static pair => pair.Key)
            .OrderBy(static id => id.Value, StringComparer.Ordinal)
            .ToArray();
    }

    public async ValueTask StopAsync(ActorId id)
    {
        if (!_actors.TryGetValue(id, out var cell))
        {
            return;
        }

        await cell.StopAsync().ConfigureAwait(false);
        _actors.TryRemove(id, out _);
        _actorIds.TryRemove(cell.RuntimeActorId, out _);
    }

    public async ValueTask<ActorStopOutcome> StopAsync(ActorId id, TimeSpan drainTimeout)
    {
        if (!_actors.TryGetValue(id, out var cell))
        {
            return ActorStopOutcome.Drained;
        }

        var result = await cell.StopAsync(drainTimeout).ConfigureAwait(false);
        _actors.TryRemove(id, out _);
        _actorIds.TryRemove(cell.RuntimeActorId, out _);
        return MapStopOutcome(result);
    }

    public async ValueTask DisposeAsync()
    {
        _actorSystem.DeadLetterPublished -= OnDeadLetterPublished;
        _actorSystem.SlowMessageDetected -= OnSlowMessageDetected;
        _actorSystem.CallTimedOut -= OnCallTimedOut;
        _actors.Clear();
        _actorIds.Clear();
        await _actorSystem.DisposeAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private ActorCell GetOrCreateCell<TActor>(ActorId id)
        where TActor : class, IActor
    {
        return GetOrCreateCell(typeof(TActor), id);
    }

    private ActorCell GetOrCreateCell(Type actorType, ActorId id)
    {
        ArgumentNullException.ThrowIfNull(actorType);
        if (!typeof(IActor).IsAssignableFrom(actorType))
        {
            throw new InvalidOperationException($"Actor type '{actorType.FullName}' must implement {typeof(IActor).FullName}.");
        }

        var cell = _actors.GetOrAdd(id, static (actorId, state) =>
        {
            var runtime = state.Runtime;
            var actorType = state.ActorType ?? throw new InvalidOperationException("Actor type is required.");
            var actor = (IActor)ActivatorUtilities.CreateInstance(runtime._services, actorType);
            var cell = new ActorCell(actorId, actor, actorType, runtime._services, runtime, runtime._options);
            var actorHandle = runtime._actorSystem.SpawnAsync(
                actorId.Value,
                new ActorAdapter(cell),
                new K.ActorSpawnOptions
                {
                    MailboxCapacity = Math.Max(1, runtime._options.MailboxCapacity)
                }).AsTask().GetAwaiter().GetResult();
            runtime._actorIds[actorHandle.Id] = actorId;
            cell.Bind(actorHandle);
            return cell;
        }, new RuntimeState(this, actorType));

        if (!IsCompatibleActorType(cell.ActorType, actorType))
        {
            throw new InvalidOperationException(
                $"Actor id '{id}' is already bound to '{cell.ActorType.FullName}', not '{actorType.FullName}'.");
        }

        return cell;
    }

    private ActorCell GetRequiredCell(Type actorType, ActorId id, string methodName)
    {
        if (TryGetCell(actorType, id, out var cell))
        {
            return cell;
        }

        throw new ActorNotFoundException(
            id,
            actorType.Name,
            methodName,
            $"Actor id '{id.Value}' is not active locally.");
    }

    private bool TryGetCell(Type actorType, ActorId id, out ActorCell cell)
    {
        ArgumentNullException.ThrowIfNull(actorType);
        if (!typeof(IActor).IsAssignableFrom(actorType))
        {
            throw new InvalidOperationException($"Actor type '{actorType.FullName}' must implement {typeof(IActor).FullName}.");
        }

        if (!_actors.TryGetValue(id, out cell!))
        {
            return false;
        }

        if (!IsCompatibleActorType(cell.ActorType, actorType))
        {
            throw new InvalidOperationException(
                $"Actor id '{id}' is already bound to '{cell.ActorType.FullName}', not '{actorType.FullName}'.");
        }

        return true;
    }

    private static bool IsCompatibleActorType(Type existingActorType, Type requestedActorType)
    {
        return existingActorType.IsAssignableTo(requestedActorType) || requestedActorType.IsAssignableFrom(existingActorType);
    }

    private void OnDeadLetterPublished(K.DeadLetter deadLetter)
    {
        var diagnostic = new ActorDeadLetterDiagnostic(
            MapActorId(deadLetter.Target),
            deadLetter.MessageType,
            deadLetter.Reason);

        foreach (var observer in _diagnosticsObservers)
        {
            try
            {
                observer.OnDeadLetter(diagnostic);
            }
            catch
            {
            }
        }

        _options.DeadLetterHandler?.Invoke(diagnostic);
    }

    private void OnSlowMessageDetected(K.SlowMessage slowMessage)
    {
        var diagnostic = new ActorSlowMessageDiagnostic(
            MapActorId(slowMessage.ActorId),
            slowMessage.MessageType,
            slowMessage.Elapsed);

        foreach (var observer in _diagnosticsObservers)
        {
            try
            {
                observer.OnSlowMessage(diagnostic);
            }
            catch
            {
            }
        }

        _options.SlowMessageHandler?.Invoke(diagnostic);
    }

    private void OnCallTimedOut(K.ActorCallTimeout timeout)
    {
        var diagnostic = new ActorCallTimeoutDiagnostic(
            timeout.Caller is { } caller ? MapActorId(caller) : null,
            MapActorId(timeout.Target),
            timeout.RequestType,
            MapCallTimeout(timeout),
            MapCallTimeoutReason(timeout.Reason),
            timeout.CallChain.Select(MapActorId).ToArray());

        foreach (var observer in _diagnosticsObservers)
        {
            try
            {
                observer.OnCallTimeout(diagnostic);
            }
            catch
            {
            }
        }

        _options.CallTimeoutHandler?.Invoke(diagnostic);
    }

    internal ActorId MapActorId(K.ActorId id)
    {
        return _actorIds.TryGetValue(id, out var actorId)
            ? actorId
            : ActorId.From(id.ToString());
    }

    private static ActorCallTimeoutReason MapCallTimeoutReason(K.ActorCallTimeoutReason reason)
    {
        return reason switch
        {
            K.ActorCallTimeoutReason.QueueTimeout => ActorCallTimeoutReason.QueueTimeout,
            _ => ActorCallTimeoutReason.ResponseTimeout
        };
    }

    private static TimeSpan MapCallTimeout(K.ActorCallTimeout timeout)
    {
        return timeout.Reason == K.ActorCallTimeoutReason.QueueTimeout
            ? timeout.QueueTimeout
            : timeout.ResponseTimeout;
    }

    private static K.ActorCallOptions CreateCallOptions(TimeSpan timeout)
    {
        return new K.ActorCallOptions(timeout, timeout);
    }

    private static ActorStopOutcome MapStopOutcome(K.ActorStopResult result)
    {
        return result == K.ActorStopResult.TimedOut
            ? ActorStopOutcome.TimedOut
            : ActorStopOutcome.Drained;
    }

    private static ActorMailboxMetrics MapMailboxMetrics(K.MailboxMetrics metrics)
    {
        return new ActorMailboxMetrics(
            metrics.Capacity,
            metrics.QueuedCount,
            metrics.EnqueuedCount,
            metrics.ProcessedCount,
            metrics.RejectedCount,
            metrics.IsCompleted);
    }

    private readonly record struct RuntimeState(LakonaActorRuntime Runtime, Type? ActorType = null);

    private sealed class KernelMessageInterceptorAdapter : K.IActorMessageInterceptor
    {
        private readonly LakonaActorRuntime _runtime;
        private readonly IActorMessageInterceptor _inner;

        public KernelMessageInterceptorAdapter(LakonaActorRuntime runtime, IActorMessageInterceptor inner)
        {
            _runtime = runtime;
            _inner = inner;
        }

        public ValueTask OnBeforeMessage(
            K.ActorId actorId,
            object message,
            CancellationToken cancellationToken)
        {
            return _inner.OnBeforeMessage(_runtime.MapActorId(actorId), message.GetType().Name, message, cancellationToken);
        }

        public ValueTask OnAfterMessage(
            K.ActorId actorId,
            object message,
            Exception? exception,
            CancellationToken cancellationToken)
        {
            return _inner.OnAfterMessage(_runtime.MapActorId(actorId), message.GetType().Name, message, exception, cancellationToken);
        }
    }

    private sealed class ActorCell
    {
        private readonly ActorId _id;
        private readonly IServiceProvider _services;
        private readonly IActorRuntime _runtime;
        private readonly ActorRuntimeOptions _runtimeOptions;
        private readonly IMessageLogStore? _messageLogStore;
        private K.ActorHandle<ActorRuntimeEnvelope>? _actorHandle;
        private int _stopping;
        private bool _activated;

        public ActorCell(
            ActorId id,
            IActor actor,
            Type actorType,
            IServiceProvider services,
            IActorRuntime runtime,
            ActorRuntimeOptions runtimeOptions)
        {
            _id = id;
            Actor = actor;
            ActorType = actorType;
            _services = services;
            _runtime = runtime;
            _runtimeOptions = runtimeOptions;
            _messageLogStore = services.GetService<IMessageLogStore>();
        }

        public IActor Actor { get; }

        public Type ActorType { get; }

        public K.ActorId RuntimeActorId
        {
            get
            {
                var actorHandle = _actorHandle ?? throw new InvalidOperationException($"Actor '{_id}' is not bound.");
                return actorHandle.Id;
            }
        }

        public void Bind(K.ActorHandle<ActorRuntimeEnvelope> actorHandle)
        {
            _actorHandle = actorHandle;
        }

        public async ValueTask EnsureActivatedAsync(CancellationToken cancellationToken)
        {
            if (_activated)
            {
                return;
            }

            await InvokeAsync(
                static async (actor, state, ct) =>
                {
                    var cell = (ActorCell)state;
                    await cell.ActivateCoreAsync(actor, ct).ConfigureAwait(false);
                    return null;
                },
                this,
                cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask<object?> InvokeAsync(
            Func<IActor, object, CancellationToken, ValueTask<object?>> callback,
            object state,
            CancellationToken cancellationToken)
        {
            if (ReferenceEquals(CurrentCell.Value, this))
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await callback(Actor, state, cancellationToken).ConfigureAwait(false);
            }

            var actorRef = (_actorHandle ?? throw new InvalidOperationException($"Actor '{_id}' is not bound.")).Ref;
            var envelope = new ActorRuntimeEnvelope(callback, state, cancellationToken);
            return await actorRef.Call<object?>(
                envelope,
                CreateCallOptions(_runtimeOptions.CallTimeout),
                cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask<bool> TryDeactivateAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            if (!_activated)
            {
                return true;
            }

            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            var actorRef = (_actorHandle ?? throw new InvalidOperationException($"Actor '{_id}' is not bound.")).Ref;
            var envelope = new ActorRuntimeEnvelope(
                static async (actor, _, ct) =>
                {
                    if (actor is Actor typedActor)
                    {
                        await typedActor.DeactivateAsync(ct).ConfigureAwait(false);
                    }

                    return null;
                },
                State: string.Empty,
                linkedCts.Token);

            try
            {
                await actorRef.Call<object?>(
                    envelope,
                    CreateCallOptions(timeout),
                    linkedCts.Token).ConfigureAwait(false);
                _activated = false;
                return true;
            }
            catch (TimeoutException)
            {
                await linkedCts.CancelAsync().ConfigureAwait(false);
                return false;
            }
            catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
            {
                return false;
            }
        }

        public ActorTellResult TryInvoke(
            Func<IActor, object, CancellationToken, ValueTask<object?>> callback,
            object state,
            CancellationToken cancellationToken)
        {
            var actorRef = (_actorHandle ?? throw new InvalidOperationException($"Actor '{_id}' is not bound.")).Ref;
            var envelope = new ActorRuntimeEnvelope(callback, state, cancellationToken);
            return MapTellResult(actorRef.TrySend(envelope));
        }

        public async ValueTask StopAsync()
        {
            var actorHandle = _actorHandle ?? throw new InvalidOperationException($"Actor '{_id}' is not bound.");
            Volatile.Write(ref _stopping, 1);
            await TryDeactivateAsync(_runtimeOptions.CallTimeout).ConfigureAwait(false);
            await actorHandle.Stop().ConfigureAwait(false);
        }

        public async ValueTask<K.ActorStopResult> StopAsync(TimeSpan drainTimeout)
        {
            var actorHandle = _actorHandle ?? throw new InvalidOperationException($"Actor '{_id}' is not bound.");
            Volatile.Write(ref _stopping, 1);
            var deactivated = await TryDeactivateAsync(drainTimeout).ConfigureAwait(false);
            var stopResult = await actorHandle.Stop(drainTimeout).ConfigureAwait(false);

            return !deactivated || stopResult == K.ActorStopResult.TimedOut
                ? K.ActorStopResult.TimedOut
                : K.ActorStopResult.Drained;
        }

        public ActorMailboxMetrics GetMailboxMetrics()
        {
            var actorHandle = _actorHandle ?? throw new InvalidOperationException($"Actor '{_id}' is not bound.");
            return MapMailboxMetrics(actorHandle.GetMailboxMetrics());
        }

        public ActorState GetState()
        {
            var actorHandle = _actorHandle;
            return actorHandle is null ? ActorState.Dead : MapActorState(actorHandle.GetState());
        }

        public IAsyncDisposable RegisterTimer(ActorRuntimeEnvelope tick, TimeSpan dueTime, TimeSpan? period)
        {
            var actorRef = (_actorHandle ?? throw new InvalidOperationException($"Actor '{_id}' is not bound.")).Ref;
            var handle = new TimerRegistrationHandle();

            if (Volatile.Read(ref _stopping) != 0)
            {
                handle.DisposeAsync().AsTask().GetAwaiter().GetResult();
                return handle;
            }

            var registration = new ActorTimerRegistration(tick, dueTime, period, handle);
            var envelope = new ActorRuntimeEnvelope(
                static (_, _, _) => new ValueTask<object?>((object?)null),
                registration,
                CancellationToken.None);

            _ = actorRef.Send(envelope);
            return handle;
        }

        public async ValueTask RegisterNativeTimerAsync(
            K.ActorKernelContext<ActorRuntimeEnvelope> ctx,
            ActorTimerRegistration registration,
            CancellationToken cancellationToken)
        {
            if (Volatile.Read(ref _stopping) != 0)
            {
                await registration.Handle.DisposeAsync().ConfigureAwait(false);
                return;
            }

            try
            {
                CurrentCell.Value = this;
                await ActivateCoreAsync(Actor, cancellationToken).ConfigureAwait(false);

                if (Volatile.Read(ref _stopping) != 0)
                {
                    await registration.Handle.DisposeAsync().ConfigureAwait(false);
                    return;
                }

                var timer = registration.Period is null
                    ? ctx.ScheduleOnce(registration.Tick, registration.DueTime)
                    : ctx.ScheduleRepeated(registration.Tick, registration.DueTime, registration.Period.Value);
                registration.Handle.Bind(timer);
            }
            finally
            {
                CurrentCell.Value = null;
            }
        }

        public async ValueTask<object?> DispatchAsync(ActorRuntimeEnvelope envelope)
        {
            if (envelope.CancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(envelope.CancellationToken);
            }

            Exception? error = null;

            try
            {
                CurrentCell.Value = this;
                await ActivateCoreAsync(Actor, envelope.CancellationToken).ConfigureAwait(false);
                return await envelope.Callback(Actor, envelope.State, envelope.CancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                error = ex;
                throw;
            }
            finally
            {
                CurrentCell.Value = null;

                if (_messageLogStore is not null)
                {
                    var entry = new MessageLogEntry(DateTimeOffset.UtcNow, envelope.State, error?.GetType().FullName);
                    _ = _messageLogStore.RecordAsync(_id, entry, CancellationToken.None);
                }
            }
        }

        private async ValueTask ActivateCoreAsync(IActor actor, CancellationToken cancellationToken)
        {
            if (_activated)
            {
                return;
            }

            if (actor is Actor typedActor)
            {
                await typedActor.ActivateAsync(
                    new ActorContext(_id, _services, _runtime),
                    cancellationToken).ConfigureAwait(false);
            }

            _activated = true;
        }

        private static ActorState MapActorState(K.ActorState state)
    {
        return state switch
        {
            K.ActorState.Draining => ActorState.Draining,
            K.ActorState.Dead => ActorState.Dead,
            _ => ActorState.Active
        };
    }

    private static ActorTellResult MapTellResult(K.ActorSendResult result)
        {
            return result switch
            {
                K.ActorSendResult.MailboxFull => ActorTellResult.MailboxFull,
                K.ActorSendResult.ActorUnavailable => ActorTellResult.ActorUnavailable,
                _ => ActorTellResult.Accepted
            };
        }
    }

    private sealed record ActorRuntimeEnvelope(
        Func<IActor, object, CancellationToken, ValueTask<object?>> Callback,
        object State,
        CancellationToken CancellationToken);

    private sealed record ActorTimerRegistration(
        ActorRuntimeEnvelope Tick,
        TimeSpan DueTime,
        TimeSpan? Period,
        TimerRegistrationHandle Handle);

    private sealed class ActorAdapter : K.IActor<ActorRuntimeEnvelope>
    {
        private readonly ActorCell _cell;

        public ActorAdapter(ActorCell cell)
        {
            _cell = cell;
        }

        public async ValueTask OnMessage(
            K.ActorKernelContext<ActorRuntimeEnvelope> ctx,
            ActorRuntimeEnvelope message)
        {
            if (message.State is ActorTimerRegistration registration)
            {
                await _cell.RegisterNativeTimerAsync(ctx, registration, message.CancellationToken).ConfigureAwait(false);
                return;
            }

            var result = await _cell.DispatchAsync(message).ConfigureAwait(false);
            if (ctx.HasPendingResponse)
            {
                ctx.Respond(result);
            }
        }
    }

    private sealed class TimerRegistrationHandle : IAsyncDisposable
    {
        private readonly object _gate = new();
        private IDisposable? _timer;
        private int _disposed;

        public void Bind(IDisposable timer)
        {
            var disposeNow = false;

            lock (_gate)
            {
                if (Volatile.Read(ref _disposed) != 0)
                {
                    disposeNow = true;
                }
                else
                {
                    _timer = timer;
                }
            }

            if (disposeNow)
            {
                timer.Dispose();
            }
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                IDisposable? timer;

                lock (_gate)
                {
                    timer = _timer;
                    _timer = null;
                }

                timer?.Dispose();
            }

            return default;
        }
    }
}
