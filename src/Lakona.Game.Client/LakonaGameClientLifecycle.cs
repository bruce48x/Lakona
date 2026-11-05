using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lakona.Game.Abstractions;
using Lakona.Game.Abstractions.Sessions;
using Lakona.Game.Client.Sessions;
using Lakona.Rpc.Client;
using Lakona.Rpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lakona.Game.Client
{
    /// <summary>Coordinates connection, recovery, and disposal for the generated Game client facade.</summary>
    /// <remarks>Owns connection generations and delegates Game session protocol state to <see cref="LakonaGameClientCore"/>.</remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed class LakonaGameClientLifecycle : IAsyncDisposable
    {
        private readonly LakonaGameClientOptions _options;
        private readonly Action<IRpcClient>? _bindCallbacks;
        private readonly LakonaGameClientCore _core;
        private readonly ReconnectableRpcClient _dispatcher = new ReconnectableRpcClient();
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly SemaphoreSlim _connectionGate = new SemaphoreSlim(1, 1);
        private readonly object _stateGate = new object();
        private RpcClientRuntime? _rpcClient;
        private SynchronizationContext? _dispatchContext;
        private Task? _recoveryTask;
        private RecoveryOperation? _recovery;
        private TaskCompletionSource<bool>? _disposal;
        private bool _connectStarted;
        private LakonaGameConnectionState _connectionState;
        private bool _apiReady;
        private readonly ILogger _connectionLogger;
        private readonly Queue<(LakonaGameConnectionStateChange Change, Action<LakonaGameConnectionStateChange> Observers)> _stateChanges = new Queue<(LakonaGameConnectionStateChange, Action<LakonaGameConnectionStateChange>)>();
        private bool _stateChangesDispatching;

        public LakonaGameClientLifecycle(LakonaGameClientOptions options, Action<IRpcClient>? bindCallbacks = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _bindCallbacks = bindCallbacks;
            _core = new LakonaGameClientCore(options.ReliablePushCursorStore);
            _core.HeartbeatCompleted += HandleHeartbeatCompleted;
            _connectionLogger = options.LoggerFactory?.CreateLogger("Lakona.Game.Client.Connection") ?? NullLogger.Instance;
        }

        public event Action<Exception?>? Disconnected;
        /// <summary>Raised for each local connection state transition after subscribing.</summary>
        /// <remarks>
        /// Notifications are serialized on the thread pool, outside lifecycle locks. Subscriber failures
        /// are logged and isolated. The payload describes the transition; ConnectionState may already
        /// be newer. UI subscribers must marshal to their main thread. Connection and disposal do not
        /// wait for subscribers, and subscribing does not replay the current state.
        /// </remarks>
        public event Action<LakonaGameConnectionStateChange>? ConnectionStateChanged;
        /// <summary>Gets the synchronized state of the current connection generation, independent of session phase.</summary>
        public LakonaGameConnectionState ConnectionState
        {
            get
            {
                lock (_stateGate)
                {
                    // RPC termination is visible before its Disconnected event finishes draining
                    // queued work. Recovery still waits for that event before replacing the connection.
                    if (_connectionState == LakonaGameConnectionState.Connected &&
                        _rpcClient!.ConnectionState != RpcClientConnectionState.Connected)
                        return LakonaGameConnectionState.Reconnecting;
                    return _connectionState;
                }
            }
        }
        public IRpcClient Dispatcher => _dispatcher;
        public ClientSessionSnapshot Snapshot => _core.Snapshot;
        public bool ReliablePushEnabled => _core.ReliablePushEnabled;
        public bool ReliablePushAckRequired => _core.ReliablePushAckRequired;
        public TimeSpan SessionResumeWindow => _core.SessionResumeWindow;

        public ValueTask StartSessionAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            return _core.StartSessionAsync(sessionId, cancellationToken);
        }

        public void EnsureApiReady()
        {
            lock (_stateGate)
            {
                // Preserve the facade's existing proxy-access boundary while queued callbacks drain.
                // ConnectionState separately reports an RPC stop before the terminal event arrives.
                if (!_apiReady)
                    throw new InvalidOperationException("LakonaGameClient is not connected. Call ConnectAsync first.");
            }
        }

        public async ValueTask ConnectAsync(CancellationToken ct = default)
        {
            ValueTask connecting;
            lock (_stateGate)
            {
                if (_disposal is not null)
                    throw new ObjectDisposedException("LakonaGameClient");
                if (_connectStarted)
                    throw new InvalidOperationException("LakonaGameClient is single-use and has already started connecting.");
                _connectStarted = true;
                SetConnectionState(LakonaGameConnectionState.Connecting);
                _dispatchContext = SynchronizationContext.Current;
                _core.MarkConnecting();
                connecting = ConnectInitialAsync(ct);
            }
            await connecting.ConfigureAwait(false);
        }

        private async ValueTask ConnectInitialAsync(CancellationToken ct)
        {
            try
            {
                await ConnectGenerationAsync(null, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                lock (_stateGate)
                {
                    _core.MarkConnectionFailed(new ClientConnectionFailure(ClientConnectionFailureKind.ConnectFailed, ex.Message));
                }
                await DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        private async ValueTask ConnectGenerationAsync(RecoveryOperation? recovery, CancellationToken ct)
        {
            var recovering = recovery is not null;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
            var token = linked.Token;
            await _connectionGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                var client = new RpcClientRuntime(_options.CreateConnectionGeneration());
                RpcClientRuntime? previous = null;
                // A candidate can disconnect before it is published as the current generation.
                bool disconnected = false;
                bool candidate = true;
                var candidateRecovery = recovery;
                client.ConnectionStateChanged += change =>
                {
                    if (change.CurrentState != RpcClientConnectionState.Stopped &&
                        change.CurrentState != RpcClientConnectionState.Disposed) return;
                    lock (_stateGate)
                    {
                        if (_disposal is null && ReferenceEquals(_rpcClient, client) &&
                            _connectionState == LakonaGameConnectionState.Connected)
                            SetConnectionState(LakonaGameConnectionState.Reconnecting);
                    }
                };
                client.Disconnected += ex =>
                {
                    lock (_stateGate)
                    {
                        disconnected = true;
                        HandleDisconnected(client, ex);
                    }
                };
                try
                {
                    client.SetDispatchSynchronizationContext(_dispatchContext);
                    _bindCallbacks?.Invoke(client);
                    await client.StartAsync(token).ConfigureAwait(false);
                    var hello = await _core.HandshakeAsync(client, new GameClientHello
                    {
                        ProtocolVersion = 1,
                        ResumeTicket = _core.ResumeTicket
                    }, token).ConfigureAwait(false);
                    lock (_stateGate) ThrowIfRecoveryEnded(recovery, token);
                    if (recovering && hello.Recovery.Status != GameSessionRecoveryStatus.Resumed)
                    {
                        FailRecovery(recovery!, new GameSessionRecoveryRejectedException(hello.Recovery.Status, hello.Recovery.Reason),
                            () => _core.ApplyRecoveryRejection(hello.Recovery));
                        throw new GameSessionRecoveryRejectedException(hello.Recovery.Status, hello.Recovery.Reason);
                    }
                    _core.BindReliablePush(client);
                    BindFrameworkNotifications(client, () => candidate && !disconnected && !token.IsCancellationRequested &&
                        (candidateRecovery is null || (ReferenceEquals(_recovery, candidateRecovery) &&
                            !candidateRecovery.Completed && !RecoveryTimeExpired(candidateRecovery))), token);
                    if (recovering)
                        await _core.CompleteRecoveryAsync(client, token).ConfigureAwait(false);
                    await _core.StopHeartbeatAsync().ConfigureAwait(false);

                    lock (_stateGate)
                    {
                        if (_disposal is not null)
                            throw new OperationCanceledException("Lakona game client is being disposed.", token);
                        ThrowIfRecoveryEnded(recovery, token);
                        if (disconnected || client.ConnectionState != RpcClientConnectionState.Connected)
                            throw new InvalidOperationException("RPC connection closed before becoming ready.");
                        previous = _rpcClient;
                        _rpcClient = client;
                        // Publish the owner before a new heartbeat can report an outcome.
                        _core.StartHeartbeat(client);
                        _dispatcher.SetCurrent(client);
                        if (recovering) _core.MarkRecovered();
                        else _core.MarkReady();
                        _apiReady = true;
                        candidate = false;
                        // Live notification handlers must not retain the retired connection through this operation.
                        candidateRecovery = null;
                        SetConnectionState(LakonaGameConnectionState.Connected);
                        if (recovery is not null)
                        {
                            recovery.Completed = true;
                            _recovery = null;
                        }
                    }
                    recovery?.StopDeadline();
                }
                catch
                {
                    lock (_stateGate)
                    {
                        candidate = false;
                        candidateRecovery = null;
                    }
                    await client.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
                // Publication completes recovery. Cleanup cannot revoke a healthy replacement.
                if (previous is not null)
                {
                    try { await previous.DisposeAsync().ConfigureAwait(false); }
                    catch (Exception error) { _connectionLogger.LogError(error, "Previous game connection cleanup failed."); }
                }
            }
            finally
            {
                _connectionGate.Release();
            }
        }

        private bool CanApplyFrameworkNotification(RpcClientRuntime client, Func<bool> isCandidate)
        {
            return _disposal is null && ((ReferenceEquals(_rpcClient, client) && _apiReady) || isCandidate());
        }

        private void BindFrameworkNotifications(RpcClientRuntime client, Func<bool> isCandidate, CancellationToken candidateToken)
        {
            client.RegisterRawNotificationHandler(GameSessionNotificationRpcIds.ServiceId,
                GameSessionNotificationRpcIds.EstablishedNotificationId, async payload =>
                {
                    CancellationToken token;
                    lock (_stateGate)
                    {
                        if (!CanApplyFrameworkNotification(client, isCandidate)) return;
                        token = ReferenceEquals(_rpcClient, client) && _apiReady ? _lifetime.Token : candidateToken;
                    }
                    var apply = await _core.PrepareGameSessionEstablishedAsync(
                        LakonaInternalCodec.DecodeGameSessionEstablished(payload), token).ConfigureAwait(false);
                    lock (_stateGate)
                    {
                        if (!CanApplyFrameworkNotification(client, isCandidate)) return;
                        apply();
                    }
                    using var acknowledgement = await client.CallRawAsync(GameSessionEstablishedRpcIds.ServiceId,
                        GameSessionEstablishedRpcIds.AckMethodId, ReadOnlyMemory<byte>.Empty, token)
                        .ConfigureAwait(false);
                });
            client.RegisterRawNotificationHandler(GameSessionNotificationRpcIds.ServiceId,
                GameSessionNotificationRpcIds.TerminatedNotificationId, payload =>
                {
                    lock (_stateGate)
                    {
                        if (CanApplyFrameworkNotification(client, isCandidate))
                            _core.ApplySessionTerminationNotice(LakonaInternalCodec.DecodeSessionTerminationNotice(payload));
                    }
                    return default;
                });
        }

        private void HandleHeartbeatCompleted(RpcClientRuntime source, GameHeartbeatReply? reply, Exception? error)
        {
            lock (_stateGate)
            {
                // Ignore retired outcomes, including replies already queued when recovery began.
                if (_disposal is not null || !_apiReady || !ReferenceEquals(_rpcClient, source))
                    return;
                if (error is null)
                    _core.ApplyHeartbeatReply(reply!);
                else
                    HandleConnectionFailure(source, error, stopConnection: true);
            }
        }

        private void HandleDisconnected(RpcClientRuntime source, Exception? ex)
        {
            HandleConnectionFailure(source, ex, stopConnection: false);
        }

        // Called under _stateGate; only the current, admitted generation may initiate recovery.
        private void HandleConnectionFailure(RpcClientRuntime source, Exception? ex, bool stopConnection)
        {
            if (_disposal is not null || !_apiReady || !ReferenceEquals(_rpcClient, source))
                return;
            _apiReady = false;
            SetConnectionState(LakonaGameConnectionState.Reconnecting);
            _dispatcher.ClearCurrent(source);
            _core.MarkReconnecting();
            var previousRecovery = _recoveryTask;
            var recovery = new RecoveryOperation(source,
                _options.RecoveryScheduler.GetUtcNow() + _core.SessionResumeWindow, ex, _lifetime.Token);
            _recovery = recovery;
            // Register every admitted generation's failure, even while the preceding
            // recovery is finishing cleanup. The worker joins its predecessor first.
            // RPC dispatch and heartbeat callbacks must never join their own loops.
            recovery.DeadlineTask = Task.Run(() => WatchRecoveryDeadlineAsync(recovery));
            _recoveryTask = Task.Run(() => RecoverConnectionAsync(previousRecovery, recovery, stopConnection));
        }

        private async Task RecoverConnectionAsync(Task? previousRecovery, RecoveryOperation recovery,
            bool stopConnection)
        {
            try
            {
                if (previousRecovery is not null)
                {
                    try { await previousRecovery.ConfigureAwait(false); }
                    catch (Exception error)
                    {
                        _connectionLogger.LogError(error, "Previous game connection recovery failed during handoff.");
                    }
                }
                lock (_stateGate) ThrowIfRecoveryEnded(recovery, recovery.Token);
                if (stopConnection)
                {
                    await _core.StopHeartbeatAsync().ConfigureAwait(false);
                    try { await recovery.Source.DisposeAsync().ConfigureAwait(false); }
                    catch (Exception error)
                    {
                        FailRecovery(recovery, error, () => _core.MarkConnectionFailed(new ClientConnectionFailure(
                            ClientConnectionFailureKind.HeartbeatFailed, error.Message)));
                        return;
                    }
                }
                await RecoverAsync(recovery).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                ExpireRecovery(recovery);
            }
            catch (Exception error)
            {
                FailRecovery(recovery, error, () => _core.MarkConnectionFailed(new ClientConnectionFailure(
                    ClientConnectionFailureKind.Disconnected, error.Message)));
            }
            finally
            {
                recovery.StopDeadline();
                try { await recovery.DeadlineTask.ConfigureAwait(false); }
                finally
                {
                    // A failed or expired recovery still owns its original connection.
                    try { await DisposeRecoverySourceAsync(recovery).ConfigureAwait(false); }
                    finally { recovery.Dispose(); }
                }
            }
        }

        private async Task RecoverAsync(RecoveryOperation recovery)
        {
            var scheduler = _options.RecoveryScheduler;
            var attempt = 0;
            while (!RecoveryTimeExpired(recovery) && !recovery.Token.IsCancellationRequested)
            {
                try
                {
                    var remaining = recovery.Deadline - scheduler.GetUtcNow();
                    if (remaining <= TimeSpan.Zero) break;
                    var delay = scheduler.GetDelay(attempt++);
                    if (delay > remaining) delay = remaining;
                    await scheduler.DelayAsync(delay, recovery.Token).ConfigureAwait(false);
                    lock (_stateGate) ThrowIfRecoveryEnded(recovery, recovery.Token);
                    await ConnectGenerationAsync(recovery, recovery.Token).ConfigureAwait(false);
                    return;
                }
                catch (OperationCanceledException) when (recovery.Token.IsCancellationRequested || RecoveryTimeExpired(recovery))
                {
                    ExpireRecovery(recovery);
                    return;
                }
                catch (GameSessionRecoveryRejectedException)
                {
                    return;
                }
                catch
                {
                }
            }
            ExpireRecovery(recovery);
        }

        // Called under _stateGate, including immediately before publishing a replacement.
        private void ThrowIfRecoveryEnded(RecoveryOperation? recovery, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (_disposal is not null || (recovery is not null &&
                (!ReferenceEquals(_recovery, recovery) || recovery.Completed || RecoveryTimeExpired(recovery))))
                throw new OperationCanceledException("Game session recovery has ended.", token);
        }

        private bool RecoveryTimeExpired(RecoveryOperation recovery) =>
            _options.RecoveryScheduler.GetUtcNow() >= recovery.Deadline;

        private async Task WatchRecoveryDeadlineAsync(RecoveryOperation recovery)
        {
            try
            {
                while (true)
                {
                    var remaining = recovery.Deadline - _options.RecoveryScheduler.GetUtcNow();
                    if (remaining <= TimeSpan.Zero) break;
                    await _options.RecoveryScheduler.DelayAsync(remaining, recovery.DeadlineToken).ConfigureAwait(false);
                }
                ExpireRecovery(recovery);
                await DisposeRecoverySourceAsync(recovery).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (recovery.DeadlineToken.IsCancellationRequested) { }
            catch (Exception error)
            {
                FailRecovery(recovery, error, () => _core.MarkConnectionFailed(new ClientConnectionFailure(
                    ClientConnectionFailureKind.Disconnected, error.Message)));
                await DisposeRecoverySourceAsync(recovery).ConfigureAwait(false);
            }
        }

        private void ExpireRecovery(RecoveryOperation recovery)
        {
            if (!RecoveryTimeExpired(recovery)) return;
            var error = new TimeoutException("Game session recovery window expired.", recovery.Reason);
            FailRecovery(recovery, error, () => _core.ApplyRecoveryRejection(new GameSessionRecoveryHandshakeResult
            {
                Status = GameSessionRecoveryStatus.StateLost,
                Reason = error.Message
            }));
        }

        private void FailRecovery(RecoveryOperation recovery, Exception? reason, Action applyFailure)
        {
            lock (_stateGate)
            {
                if (_disposal is not null || !ReferenceEquals(_recovery, recovery) || recovery.Completed) return;
                recovery.Completed = true;
                _recovery = null;
                applyFailure();
                _apiReady = false;
                SetConnectionState(LakonaGameConnectionState.Disconnected);
            }
            recovery.StopDeadline();
            recovery.Cancel();
            Disconnected?.Invoke(reason);
        }

        private async Task DisposeRecoverySourceAsync(RecoveryOperation recovery)
        {
            try
            {
                await _core.StopHeartbeatAsync(recovery.Source).ConfigureAwait(false);
                await recovery.Source.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception error) { _connectionLogger.LogError(error, "Failed game connection cleanup failed."); }
        }

        private sealed class RecoveryOperation : IDisposable
        {
            private readonly CancellationTokenSource _cancellation;
            private readonly CancellationTokenSource _deadlineCancellation;

            public RecoveryOperation(RpcClientRuntime source, DateTimeOffset deadline, Exception? reason, CancellationToken lifetime)
            {
                Source = source;
                Deadline = deadline;
                Reason = reason;
                _cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
                _deadlineCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
                Token = _cancellation.Token;
                DeadlineToken = _deadlineCancellation.Token;
            }

            public RpcClientRuntime Source { get; }
            public DateTimeOffset Deadline { get; }
            public Exception? Reason { get; }
            public CancellationToken Token { get; }
            public CancellationToken DeadlineToken { get; }
            public Task DeadlineTask { get; set; } = Task.CompletedTask;
            public bool Completed { get; set; } // Only read or changed under _stateGate.
            public void Cancel() => _cancellation.Cancel();
            public void StopDeadline() => _deadlineCancellation.Cancel();
            public void Dispose()
            {
                _deadlineCancellation.Dispose();
                _cancellation.Dispose();
            }
        }

        public ValueTask DisposeAsync()
        {
            TaskCompletionSource<bool> completion;
            lock (_stateGate)
            {
                if (_disposal is not null) return new ValueTask(_disposal.Task);
                completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _disposal = completion;
                // Do not skip an already visible stop if its RPC notification is still queued.
                if (_connectionState == LakonaGameConnectionState.Connected &&
                    _rpcClient!.ConnectionState != RpcClientConnectionState.Connected)
                    SetConnectionState(LakonaGameConnectionState.Reconnecting);
                _apiReady = false;
                SetConnectionState(LakonaGameConnectionState.Disposed);
                if (_rpcClient is not null) _dispatcher.ClearCurrent(_rpcClient);
            }
            _ = CompleteDisposalAsync(completion);
            return new ValueTask(completion.Task);
        }

        private async Task CompleteDisposalAsync(TaskCompletionSource<bool> completion)
        {
            try
            {
                _lifetime.Cancel();
                Task? recovery;
                lock (_stateGate) recovery = _recoveryTask;
                try
                {
                    if (recovery is not null) await recovery.ConfigureAwait(false);
                }
                finally
                {
                    // Also joins an initial connection that is still creating or handshaking.
                    await _connectionGate.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        try { await _core.DisposeAsync().ConfigureAwait(false); }
                        finally
                        {
                            if (_rpcClient is not null) await _rpcClient.DisposeAsync().ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        _connectionGate.Dispose();
                        _lifetime.Dispose();
                    }
                }
                completion.TrySetResult(true);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }

        // Called under _stateGate; application observers never run under this lock.
        private void SetConnectionState(LakonaGameConnectionState state)
        {
            if (_connectionState == state) return;
            var change = new LakonaGameConnectionStateChange(_connectionState, state);
            _connectionState = state;
            var observers = ConnectionStateChanged;
            if (observers is null) return;
            _stateChanges.Enqueue((change, observers));
            if (_stateChangesDispatching) return;
            _stateChangesDispatching = true;
            ThreadPool.QueueUserWorkItem(_ => DispatchStateChanges());
        }

        private void DispatchStateChanges()
        {
            while (true)
            {
                (LakonaGameConnectionStateChange Change, Action<LakonaGameConnectionStateChange> Observers) notification;
                lock (_stateGate)
                {
                    if (_stateChanges.Count == 0) { _stateChangesDispatching = false; return; }
                    notification = _stateChanges.Dequeue();
                }
                foreach (Action<LakonaGameConnectionStateChange> observer in notification.Observers.GetInvocationList())
                {
                    try { observer(notification.Change); }
                    catch (Exception ex)
                    {
                        _connectionLogger.LogError(ex, "Game connection state subscriber failed for {PreviousState} -> {CurrentState}.",
                            notification.Change.PreviousState, notification.Change.CurrentState);
                    }
                }
            }
        }
    }
}
