using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Lakona.Game.Server.Diagnostics;

namespace Lakona.Game.Server.Actors;

public sealed class LakonaActorRuntime : IActorRuntime, IDisposable, IAsyncDisposable
{
    private static readonly AsyncLocal<ActorCell?> CurrentCell = new();

    private readonly ConcurrentDictionary<ActorId, ActorCell> _actors = new();
    private readonly ConcurrentDictionary<global::Lakona.Actor.ActorId, ActorId> _actorIds = new();
    private readonly IServiceProvider _services;
    private readonly ActorRuntimeOptions _options;
    private readonly global::Lakona.Actor.ActorSystem _actorSystem;

    public LakonaActorRuntime(IServiceProvider services, ActorRuntimeOptions options)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _actorSystem = new global::Lakona.Actor.ActorSystem(new global::Lakona.Actor.ActorSystemOptions
        {
            MailboxCapacity = Math.Max(1, options.MailboxCapacity),
            SlowMessageThreshold = options.SlowMessageThreshold,
            MessageInterceptor = options.MessageInterceptor
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

    public async ValueTask TellAsync<TActor>(
        ActorId id,
        Func<TActor, CancellationToken, ValueTask> message,
        CancellationToken cancellationToken = default)
        where TActor : class, IActor
    {
        ArgumentNullException.ThrowIfNull(message);

        var cell = GetOrCreateCell<TActor>(id);
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

        var cell = GetOrCreateCell<TActor>(id);
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

        var cell = GetOrCreateCell<TActor>(id);
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

    public IAsyncDisposable RegisterTimer<TActor>(
        ActorId id,
        TimeSpan dueTime,
        TimeSpan? period,
        Func<TActor, CancellationToken, ValueTask> callback)
        where TActor : class, IActor
    {
        ArgumentNullException.ThrowIfNull(callback);

        var cell = GetOrCreateCell<TActor>(id);
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
        var cell = _actors.GetOrAdd(id, static (actorId, state) =>
        {
            var runtime = state.Runtime;
            var actor = ActivatorUtilities.CreateInstance<TActor>(runtime._services);
            var cell = new ActorCell(actorId, actor, typeof(TActor), runtime._services, runtime, runtime._options);
            var actorHandle = runtime._actorSystem.SpawnAsync(
                actorId.Value,
                new ActorAdapter(cell),
                new global::Lakona.Actor.ActorSpawnOptions
                {
                    MailboxCapacity = Math.Max(1, runtime._options.MailboxCapacity)
                }).AsTask().GetAwaiter().GetResult();
            runtime._actorIds[actorHandle.Id] = actorId;
            cell.Bind(actorHandle);
            return cell;
        }, new RuntimeState(this));

        if (!cell.ActorType.IsAssignableTo(typeof(TActor)) && !typeof(TActor).IsAssignableFrom(cell.ActorType))
        {
            throw new InvalidOperationException(
                $"Actor id '{id}' is already bound to '{cell.ActorType.FullName}', not '{typeof(TActor).FullName}'.");
        }

        return cell;
    }

    private void OnDeadLetterPublished(global::Lakona.Actor.DeadLetter deadLetter)
    {
        _options.DeadLetterHandler?.Invoke(new ActorDeadLetterDiagnostic(
            MapActorId(deadLetter.Target),
            deadLetter.MessageType,
            deadLetter.Reason));
    }

    private void OnSlowMessageDetected(global::Lakona.Actor.SlowMessage slowMessage)
    {
        _options.SlowMessageHandler?.Invoke(new ActorSlowMessageDiagnostic(
            MapActorId(slowMessage.ActorId),
            slowMessage.MessageType,
            slowMessage.Elapsed));
    }

    private void OnCallTimedOut(global::Lakona.Actor.ActorCallTimeout timeout)
    {
        _options.CallTimeoutHandler?.Invoke(new ActorCallTimeoutDiagnostic(
            timeout.Caller is { } caller ? MapActorId(caller) : null,
            MapActorId(timeout.Target),
            timeout.RequestType,
            MapCallTimeout(timeout),
            MapCallTimeoutReason(timeout.Reason),
            timeout.CallChain.Select(MapActorId).ToArray()));
    }

    private ActorId MapActorId(global::Lakona.Actor.ActorId id)
    {
        return _actorIds.TryGetValue(id, out var actorId)
            ? actorId
            : ActorId.From(id.ToString());
    }

    private static ActorCallTimeoutReason MapCallTimeoutReason(global::Lakona.Actor.ActorCallTimeoutReason reason)
    {
        return reason switch
        {
            global::Lakona.Actor.ActorCallTimeoutReason.QueueTimeout => ActorCallTimeoutReason.QueueTimeout,
            _ => ActorCallTimeoutReason.ResponseTimeout
        };
    }

    private static TimeSpan MapCallTimeout(global::Lakona.Actor.ActorCallTimeout timeout)
    {
        return timeout.Reason == global::Lakona.Actor.ActorCallTimeoutReason.QueueTimeout
            ? timeout.QueueTimeout
            : timeout.ResponseTimeout;
    }

    private static global::Lakona.Actor.ActorCallOptions CreateCallOptions(TimeSpan timeout)
    {
        return new global::Lakona.Actor.ActorCallOptions(timeout, timeout);
    }

    private static ActorStopOutcome MapStopOutcome(global::Lakona.Actor.ActorStopResult result)
    {
        return result == global::Lakona.Actor.ActorStopResult.TimedOut
            ? ActorStopOutcome.TimedOut
            : ActorStopOutcome.Drained;
    }

    private static ActorMailboxMetrics MapMailboxMetrics(global::Lakona.Actor.MailboxMetrics metrics)
    {
        return new ActorMailboxMetrics(
            metrics.Capacity,
            metrics.QueuedCount,
            metrics.EnqueuedCount,
            metrics.ProcessedCount,
            metrics.RejectedCount,
            metrics.IsCompleted);
    }

    private readonly record struct RuntimeState(LakonaActorRuntime Runtime);

    private sealed class ActorCell
    {
        private readonly ActorId _id;
        private readonly IServiceProvider _services;
        private readonly IActorRuntime _runtime;
        private readonly ActorRuntimeOptions _runtimeOptions;
        private readonly IMessageLogStore? _messageLogStore;
        private global::Lakona.Actor.ActorHandle<ActorRuntimeEnvelope>? _actorHandle;
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

        public global::Lakona.Actor.ActorId RuntimeActorId
        {
            get
            {
                var actorHandle = _actorHandle ?? throw new InvalidOperationException($"Actor '{_id}' is not bound.");
                return actorHandle.Id;
            }
        }

        public void Bind(global::Lakona.Actor.ActorHandle<ActorRuntimeEnvelope> actorHandle)
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

        public async ValueTask<global::Lakona.Actor.ActorStopResult> StopAsync(TimeSpan drainTimeout)
        {
            var actorHandle = _actorHandle ?? throw new InvalidOperationException($"Actor '{_id}' is not bound.");
            Volatile.Write(ref _stopping, 1);
            var deactivated = await TryDeactivateAsync(drainTimeout).ConfigureAwait(false);
            var stopResult = await actorHandle.Stop(drainTimeout).ConfigureAwait(false);

            return !deactivated || stopResult == global::Lakona.Actor.ActorStopResult.TimedOut
                ? global::Lakona.Actor.ActorStopResult.TimedOut
                : global::Lakona.Actor.ActorStopResult.Drained;
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
                static (_, _, _) => ValueTask.FromResult<object?>(null),
                registration,
                CancellationToken.None);

            _ = actorRef.Send(envelope);
            return handle;
        }

        public async ValueTask RegisterNativeTimerAsync(
            global::Lakona.Actor.ActorContext<ActorRuntimeEnvelope> ctx,
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

        private static ActorState MapActorState(global::Lakona.Actor.ActorState state)
    {
        return state switch
        {
            global::Lakona.Actor.ActorState.Draining => ActorState.Draining,
            global::Lakona.Actor.ActorState.Dead => ActorState.Dead,
            _ => ActorState.Active
        };
    }

    private static ActorTellResult MapTellResult(global::Lakona.Actor.ActorSendResult result)
        {
            return result switch
            {
                global::Lakona.Actor.ActorSendResult.MailboxFull => ActorTellResult.MailboxFull,
                global::Lakona.Actor.ActorSendResult.ActorUnavailable => ActorTellResult.ActorUnavailable,
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

    private sealed class ActorAdapter : global::Lakona.Actor.IActor<ActorRuntimeEnvelope>
    {
        private readonly ActorCell _cell;

        public ActorAdapter(ActorCell cell)
        {
            _cell = cell;
        }

        public async ValueTask OnMessage(
            global::Lakona.Actor.ActorContext<ActorRuntimeEnvelope> ctx,
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

            return ValueTask.CompletedTask;
        }
    }
}
