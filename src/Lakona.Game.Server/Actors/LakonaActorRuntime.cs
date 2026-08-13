using System.Collections.Concurrent;
using Lakona.Game.Server.Actors.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Actors;

public sealed class LakonaActorRuntime : IActorRuntime, IActorHostingRuntime, IDisposable, IAsyncDisposable
{
    private static readonly AsyncLocal<ActorTurnScope?> CurrentTurn = new();

    private readonly ConcurrentDictionary<ActorId, ActorCell> _actors = new();
    private readonly AsyncLocal<ActorCallContext?> _currentCallContext = new();
    private readonly object _disposeGate = new();
    private readonly IServiceProvider _services;
    private readonly ActorRuntimeOptions _options;
    private readonly ActorRuntimeDiagnosticsPublisher _diagnostics;
    private Task? _disposeTask;
    private int _disposeState;

    public LakonaActorRuntime(
        IServiceProvider services,
        ActorRuntimeOptions options)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (options.MailboxCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "MailboxCapacity must be greater than zero.");
        }

        if (options.CallTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "CallTimeout must be greater than zero.");
        }

        if (options.SlowMessageThreshold is { } slowMessageThreshold &&
            slowMessageThreshold <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "SlowMessageThreshold must be greater than zero when set.");
        }

        _diagnostics = new ActorRuntimeDiagnosticsPublisher(options);
    }

    bool IActorHostingRuntime.TryGetLocalActor(ActorId actorId, out Type actorType, out ActorState state)
    {
        ThrowIfDisposed();
        if (_actors.TryGetValue(actorId, out var cell))
        {
            actorType = cell.ActorType;
            state = cell.GetState();
            return true;
        }

        actorType = typeof(IActor);
        state = ActorState.Dead;
        return false;
    }

    bool IActorHostingRuntime.IsExactLocalActor(ActorId actorId, object actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ThrowIfDisposed();
        return _actors.TryGetValue(actorId, out var cell) && ReferenceEquals(cell.Actor, actor);
    }

    void IActorHostingRuntime.KeepLocalAdmissionClosed(Type actorType, ActorId actorId, object actor)
    {
        ArgumentNullException.ThrowIfNull(actorType);
        ArgumentNullException.ThrowIfNull(actor);
        if (Volatile.Read(ref _disposeState) == 0
            && _actors.TryGetValue(actorId, out var cell)
            && cell.ActorType == actorType
            && ReferenceEquals(cell.Actor, actor))
        {
            cell.BeginStopping();
        }
    }

    async ValueTask IActorHostingRuntime.InvokeLocalAsync(
        Type actorType,
        ActorId actorId,
        Func<object, CancellationToken, ValueTask> callback,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actorType);
        ArgumentNullException.ThrowIfNull(callback);
        ThrowIfDisposed();

        var cell = GetRequiredCell(actorType, actorId, nameof(IActorHostingRuntime.InvokeLocalAsync));
        await cell.InvokeLifecycleAsync(
            static async (actor, state, ct) =>
            {
                var callback = (Func<object, CancellationToken, ValueTask>)state;
                await callback(actor, ct).ConfigureAwait(false);
                return null;
            },
            callback,
            cancellationToken).ConfigureAwait(false);
    }

    ValueTask IActorHostingRuntime.OpenLocalAdmissionAsync(
        Type actorType,
        ActorId actorId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        GetRequiredCell(actorType, actorId, nameof(IActorHostingRuntime.OpenLocalAdmissionAsync)).OpenAdmission();
        return default;
    }

    async ValueTask<ActorHostingLocalCreateResult> IActorHostingRuntime.CreateLocalAsync(
        Type actorType,
        ActorId actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actorType);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        if (_actors.TryGetValue(actorId, out var existing))
        {
            return IsExactActorType(existing.ActorType, actorType)
                ? new ActorHostingLocalCreateResult(
                    ActorHostingLocalCreateStatus.AlreadyExistsSameType,
                    actorId,
                    actorType)
                : new ActorHostingLocalCreateResult(
                    ActorHostingLocalCreateStatus.AlreadyExistsDifferentType,
                    actorId,
                    actorType,
                    existing.ActorType);
        }

        var cell = CreateCell(actorType, actorId);
        bool added;
        lock (_disposeGate)
        {
            if (_disposeState != 0)
            {
                added = false;
            }
            else
            {
                added = _actors.TryAdd(actorId, cell);
            }
        }

        if (!added)
        {
            cell.RequestStop();
            await cell.Completion.ConfigureAwait(false);
            ThrowIfDisposed();
            if (_actors.TryGetValue(actorId, out existing))
            {
                return IsExactActorType(existing.ActorType, actorType)
                    ? new ActorHostingLocalCreateResult(
                        ActorHostingLocalCreateStatus.AlreadyExistsSameType,
                        actorId,
                        actorType)
                    : new ActorHostingLocalCreateResult(
                        ActorHostingLocalCreateStatus.AlreadyExistsDifferentType,
                        actorId,
                        actorType,
                        existing.ActorType);
            }

            throw new InvalidOperationException($"Actor id '{actorId.Value}' could not be reserved.");
        }

        try
        {
            await cell.EnsureActivatedAsync(cancellationToken).ConfigureAwait(false);
            return new ActorHostingLocalCreateResult(
                ActorHostingLocalCreateStatus.Created,
                actorId,
                actorType);
        }
        catch
        {
            _actors.TryRemove(new KeyValuePair<ActorId, ActorCell>(actorId, cell));
            try
            {
                await cell.StopAsync().ConfigureAwait(false);
            }
            catch
            {
            }

            throw;
        }
    }

    async ValueTask<ActorHostingLocalDestroyResult> IActorHostingRuntime.DestroyLocalAsync(
        Type actorType,
        ActorId actorId,
        TimeSpan drainTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actorType);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        if (!_actors.TryGetValue(actorId, out var cell))
        {
            return new ActorHostingLocalDestroyResult(ActorHostingLocalDestroyStatus.NotFound, actorId, actorType);
        }

        if (!IsExactActorType(cell.ActorType, actorType))
        {
            return new ActorHostingLocalDestroyResult(
                ActorHostingLocalDestroyStatus.TypeMismatch,
                actorId,
                actorType,
                cell.ActorType);
        }

        var timedOut = await StopCoreAsync(actorId, drainTimeout).ConfigureAwait(false);
        return timedOut
            ? new ActorHostingLocalDestroyResult(ActorHostingLocalDestroyStatus.TimedOut, actorId, actorType)
            : new ActorHostingLocalDestroyResult(ActorHostingLocalDestroyStatus.Destroyed, actorId, actorType);
    }

    async ValueTask<ActorHostingLocalRetireResult> IActorHostingRuntime.RetireLocalAsync(
        Type actorType,
        ActorId actorId,
        Func<object, CancellationToken, ValueTask> stop,
        TimeSpan drainTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actorType);
        ArgumentNullException.ThrowIfNull(stop);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        if (!_actors.TryGetValue(actorId, out var cell))
            return new(ActorHostingLocalRetireStatus.NotFound, actorId, actorType);
        if (!IsExactActorType(cell.ActorType, actorType))
            return new(ActorHostingLocalRetireStatus.TypeMismatch, actorId, actorType, cell.ActorType);

        var retired = await cell.RetireAsync(stop, drainTimeout, cancellationToken).ConfigureAwait(false);
        if (!retired)
        {
            // Keep the exact retired cell reserved. A later Destroy retry must
            // prove lifecycle completion before Actor Location can be released.
        }
        return new(
            retired ? ActorHostingLocalRetireStatus.Retired : ActorHostingLocalRetireStatus.TimedOut,
            actorId,
            actorType);
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

    public async ValueTask<object?> AskAsync(
        Type actorType,
        ActorId id,
        Func<IActor, CancellationToken, ValueTask<object?>> message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actorType);
        ArgumentNullException.ThrowIfNull(message);

        var cell = GetRequiredCell(actorType, id, nameof(AskAsync));
        return await cell.InvokeAsync(
            static async (actor, state, ct) =>
            {
                var callback = (Func<IActor, CancellationToken, ValueTask<object?>>)state;
                return await callback(actor, ct).ConfigureAwait(false);
            },
            message,
            cancellationToken).ConfigureAwait(false);
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

    public bool TryGetMailboxMetrics(ActorId id, out ActorMailboxMetrics metrics)
    {
        ThrowIfDisposed();
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
        ThrowIfDisposed();
        if (_actors.TryGetValue(id, out var cell))
        {
            return cell.GetState();
        }

        return ActorState.Dead;
    }

    public ActorRuntimeDiagnosticsSnapshot GetDiagnosticsSnapshot()
    {
        ThrowIfDisposed();
        var actors = new List<ActorDiagnosticsCellSnapshot>();
        foreach (var cell in _actors.Values)
        {
            if (TryCaptureActorDiagnosticsCell(cell, out var actor))
            {
                actors.Add(actor);
            }
        }

        var actorTypes = actors
            .GroupBy(static actor => actor.ActorType)
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

    private static bool TryCaptureActorDiagnosticsCell(
        ActorCell cell,
        out ActorDiagnosticsCellSnapshot snapshot)
    {
        snapshot = default;

        if (!cell.TryGetState(out var state) || state != ActorState.Active)
        {
            return false;
        }

        if (!cell.TryGetMailboxMetrics(out var metrics))
        {
            return false;
        }

        if (!cell.TryGetState(out state) || state != ActorState.Active)
        {
            return false;
        }

        snapshot = new ActorDiagnosticsCellSnapshot(
            cell.ActorType.FullName ?? cell.ActorType.Name,
            metrics);
        return true;
    }

    public IReadOnlyList<ActorId> GetActiveActorIds(Type actorType)
    {
        ArgumentNullException.ThrowIfNull(actorType);
        ThrowIfDisposed();
        return _actors
            .Where(pair => actorType.IsAssignableFrom(pair.Value.ActorType) && pair.Value.GetState() == ActorState.Active)
            .Select(static pair => pair.Key)
            .OrderBy(static id => id.Value, StringComparer.Ordinal)
            .ToArray();
    }

    private async ValueTask<bool> StopCoreAsync(ActorId id, TimeSpan drainTimeout)
    {
        if (!_actors.TryGetValue(id, out var cell))
        {
            return false;
        }

        var result = await cell.StopAsync(drainTimeout).ConfigureAwait(false);
        if (result != ActorMailboxStopResult.TimedOut)
        {
            _actors.TryRemove(new KeyValuePair<ActorId, ActorCell>(id, cell));
            return false;
        }

        _ = RemoveCellWhenCompletedAsync(id, cell);
        return true;
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate)
        {
            if (_disposeTask is null)
            {
                Volatile.Write(ref _disposeState, 1);
                ActorCell[] cells = _actors.Values.ToArray();
                _actors.Clear();
                _disposeTask = DisposeCoreAsync(cells);
            }

            return new ValueTask(_disposeTask);
        }
    }

    private static async Task DisposeCoreAsync(ActorCell[] cells)
    {
        foreach (ActorCell cell in cells)
        {
            cell.RequestStop();
        }

        await Task.WhenAll(cells.Select(static cell => cell.Completion)).ConfigureAwait(false);
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private ActorCell CreateCell(Type actorType, ActorId id)
    {
        ArgumentNullException.ThrowIfNull(actorType);
        if (!typeof(IActor).IsAssignableFrom(actorType))
        {
            throw new InvalidOperationException($"Actor type '{actorType.FullName}' must implement {typeof(IActor).FullName}.");
        }

        var actor = (IActor)ActivatorUtilities.CreateInstance(_services, actorType);
        var cell = new ActorCell(
            id,
            actor,
            actorType,
            _services,
            this,
            _options,
            _diagnostics,
            GetCurrentCallContext,
            SetCurrentCallContext);
        return cell;
    }

    private ActorCell GetRequiredCell(Type actorType, ActorId id, string methodName)
    {
        ThrowIfDisposed();
        if (TryGetCell(actorType, id, out var cell))
        {
            return cell;
        }

        throw ActorNotFoundException.BeforeDispatch(
            id,
            actorType.Name,
            methodName,
            $"Actor id '{id.Value}' is not active locally.");
    }

    private bool TryGetCell(Type actorType, ActorId id, out ActorCell cell)
    {
        ThrowIfDisposed();
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
        return requestedActorType.IsAssignableFrom(existingActorType);
    }

    private static bool IsExactActorType(Type existingActorType, Type requestedActorType)
    {
        return existingActorType == requestedActorType;
    }

    private ActorCallContext? GetCurrentCallContext()
    {
        return _currentCallContext.Value;
    }

    private void SetCurrentCallContext(ActorCallContext? context)
    {
        _currentCallContext.Value = context;
    }

    private async Task RemoveCellWhenCompletedAsync(ActorId id, ActorCell cell)
    {
        try
        {
            await cell.Completion.ConfigureAwait(false);
        }
        catch
        {
            // A terminal mailbox fault must not reserve the public actor id forever.
        }
        finally
        {
            _actors.TryRemove(new KeyValuePair<ActorId, ActorCell>(id, cell));
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
    }

    private readonly record struct ActorDiagnosticsCellSnapshot(
        string ActorType,
        ActorMailboxMetrics Metrics);

    private sealed class ActorCell
    {
        private readonly ActorId _id;
        private readonly IServiceProvider _services;
        private readonly IActorRuntime _runtime;
        private readonly ActorRuntimeOptions _runtimeOptions;
        private readonly ActorMailbox _mailbox;
        private bool _activated;
        private bool _retired;

        public ActorCell(
            ActorId id,
            IActor actor,
            Type actorType,
            IServiceProvider services,
            IActorRuntime runtime,
            ActorRuntimeOptions runtimeOptions,
            ActorRuntimeDiagnosticsPublisher diagnostics,
            Func<ActorCallContext?> getCurrentCallContext,
            Action<ActorCallContext?> setCurrentCallContext)
        {
            _id = id;
            Actor = actor;
            ActorType = actorType;
            _services = services;
            _runtime = runtime;
            _runtimeOptions = runtimeOptions;
            _mailbox = new ActorMailbox(
                id,
                actorType,
                runtimeOptions.MailboxCapacity,
                runtimeOptions.SlowMessageThreshold,
                DispatchAsync,
                getCurrentCallContext,
                setCurrentCallContext,
                diagnostics);
        }

        public IActor Actor { get; }

        public Type ActorType { get; }

        public Task Completion => _mailbox.Completion;

        public async ValueTask EnsureActivatedAsync(CancellationToken cancellationToken)
        {
            if (_activated)
            {
                return;
            }

            await InvokeLifecycleAsync(
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
            if (CurrentTurn.Value is { IsActive: true } turn &&
                ReferenceEquals(turn.Cell, this))
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await callback(Actor, state, cancellationToken).ConfigureAwait(false);
            }

            ActorWorkItem work = new(callback, state, cancellationToken);
            return await _mailbox.CallAsync(
                work,
                _runtimeOptions.CallTimeout,
                _runtimeOptions.CallTimeout,
                cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask<object?> InvokeLifecycleAsync(
            Func<IActor, object, CancellationToken, ValueTask<object?>> callback,
            object state,
            CancellationToken cancellationToken)
        {
            ActorWorkItem work = new(callback, state, cancellationToken);
            return await _mailbox.CallAsync(
                work,
                _runtimeOptions.CallTimeout,
                _runtimeOptions.CallTimeout,
                cancellationToken,
                allowStopping: true).ConfigureAwait(false);
        }

        public void OpenAdmission() => _mailbox.OpenAdmission();

        public void BeginStopping() => _mailbox.BeginStopping();

        public async ValueTask<bool> TryDeactivateAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            if (!_activated)
            {
                return true;
            }

            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            ActorWorkItem work = new(
                static async (actor, _, ct) =>
                {
                    if (actor is Actor typedActor)
                    {
                        await typedActor.DeactivateAsync(ct).ConfigureAwait(false);
                    }

                    return null;
                },
                string.Empty,
                linkedCts.Token);

            try
            {
                await _mailbox.CallAsync(
                    work,
                    timeout,
                    timeout,
                    linkedCts.Token,
                    allowStopping: true).ConfigureAwait(false);
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
            ActorWorkItem work = new(callback, state, cancellationToken);
            return _mailbox.TryPost(work);
        }

        public async ValueTask StopAsync()
        {
            _mailbox.BeginStopping();
            try
            {
                await TryDeactivateAsync(_runtimeOptions.CallTimeout).ConfigureAwait(false);
            }
            catch
            {
                _mailbox.CancelStopping();
                throw;
            }

            await _mailbox.RequestStopAsync().ConfigureAwait(false);
        }

        public async ValueTask<ActorMailboxStopResult> StopAsync(TimeSpan drainTimeout)
        {
            _mailbox.BeginStopping();
            try
            {
                await TryDeactivateAsync(drainTimeout).ConfigureAwait(false);
            }
            catch
            {
                _mailbox.CancelStopping();
                throw;
            }

            return await _mailbox.StopAsync(drainTimeout).ConfigureAwait(false);
        }

        public async ValueTask<bool> RetireAsync(
            Func<object, CancellationToken, ValueTask> stop,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            _mailbox.BeginStopping();
            if (_retired) return true;
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            try
            {
                ActorWorkItem work = new(
                    static async (actor, state, ct) =>
                    {
                        await ((Func<object, CancellationToken, ValueTask>)state)(actor, ct).ConfigureAwait(false);
                        if (actor is Actor typedActor)
                        {
                            await typedActor.DeactivateAsync(ct).ConfigureAwait(false);
                        }
                        return null;
                    },
                    stop,
                    linkedCts.Token);
                await _mailbox.CallAsync(
                    work,
                    timeout,
                    timeout,
                    linkedCts.Token,
                    allowStopping: true).ConfigureAwait(false);
                _activated = false;
                _retired = true;
                return true;
            }
            catch (TimeoutException)
            {
                // ActorMailbox owns an independent response-timeout timer. Under
                // scheduler pressure it can win the race with timeoutCts. Cancel
                // the queued lifecycle work explicitly before returning, or it
                // can run after the caller has observed a timed-out retirement.
                await linkedCts.CancelAsync().ConfigureAwait(false);
                return false;
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return false;
            }
            catch
            {
                _mailbox.CancelStopping();
                throw;
            }
        }

        public ActorMailboxMetrics GetMailboxMetrics()
        {
            return _mailbox.GetMetrics();
        }

        public bool TryGetMailboxMetrics(out ActorMailboxMetrics metrics)
        {
            if (!TryGetState(out ActorState state) || state != ActorState.Active)
            {
                metrics = default;
                return false;
            }

            metrics = _mailbox.GetMetrics();
            return true;
        }

        public bool TryGetState(out ActorState state)
        {
            state = GetState();
            return true;
        }

        public ActorState GetState()
        {
            return _mailbox.State;
        }

        public void RequestStop()
        {
            _ = _mailbox.RequestStopAsync();
        }

        private async ValueTask<object?> DispatchAsync(ActorWorkItem work)
        {
            if (work.CancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(work.CancellationToken);
            }

            ActorTurnScope? previousTurn = CurrentTurn.Value;
            ActorTurnScope currentTurn = new(this);

            try
            {
                CurrentTurn.Value = currentTurn;
                await ActivateCoreAsync(Actor, work.CancellationToken).ConfigureAwait(false);
                var result = await work.Callback(Actor, work.State, work.CancellationToken).ConfigureAwait(false);
                if (currentTurn.DeactivationRequested)
                {
                    _mailbox.BeginStopping();
                    ThreadPool.UnsafeQueueUserWorkItem(
                        static state => _ = ((ActorCell)state!).DeactivateSelfAsync(),
                        this);
                }

                return result;
            }
            finally
            {
                currentTurn.Deactivate();
                CurrentTurn.Value = previousTurn;
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
                    new ActorContext(
                        _id,
                        _services,
                        _runtime,
                        () =>
                        {
                            if (CurrentTurn.Value is not { IsActive: true } turn
                                || !ReferenceEquals(turn.Cell, this))
                            {
                                throw new InvalidOperationException(
                                    "Actor deactivation can only be requested from the actor's active turn.");
                            }

                            turn.RequestDeactivation();
                        }),
                    cancellationToken).ConfigureAwait(false);
            }

            _activated = true;
        }

        private async Task DeactivateSelfAsync()
        {
            try
            {
                var sink = _services.GetRequiredService<IActorSelfDeactivationSink>();
                await sink.DeactivateAsync(ActorType, _id, Actor).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                ((IActorHostingRuntime)_runtime).KeepLocalAdmissionClosed(ActorType, _id, Actor);
                // Explicit destruction remains available to retry. A failed
                // background attempt must not terminate the mailbox processor.
                _services.GetService<ILogger<LakonaActorRuntime>>()?.LogError(
                    exception,
                    "An Actor failed to complete its requested deactivation and remains draining until explicit destruction is retried.");
            }
        }
    }

    private sealed class ActorTurnScope(ActorCell cell)
    {
        private int _active = 1;
        private int _deactivationRequested;

        internal ActorCell Cell { get; } = cell;

        internal bool IsActive => Volatile.Read(ref _active) != 0;

        internal bool DeactivationRequested => Volatile.Read(ref _deactivationRequested) != 0;

        internal void RequestDeactivation()
        {
            if (!IsActive)
            {
                throw new InvalidOperationException("The actor turn has already completed.");
            }

            Volatile.Write(ref _deactivationRequested, 1);
        }

        internal void Deactivate()
        {
            Volatile.Write(ref _active, 0);
        }
    }
}
