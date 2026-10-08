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
                await ConnectGenerationAsync(false, ct).ConfigureAwait(false);
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

        private async ValueTask ConnectGenerationAsync(bool recovering, CancellationToken ct)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
            var token = linked.Token;
            await _connectionGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                var client = new RpcClientRuntime(_options.CreateConnectionGeneration());
                // A candidate can disconnect before it is published as the current generation.
                bool disconnected = false;
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
                    if (recovering && hello.Recovery.Status != GameSessionRecoveryStatus.Resumed)
                    {
                        _core.ApplyRecoveryRejection(hello.Recovery);
                        throw new GameSessionRecoveryRejectedException(hello.Recovery.Status, hello.Recovery.Reason);
                    }
                    _core.BindReliablePush(client);
                    BindFrameworkNotifications(client);
                    if (recovering)
                        await _core.CompleteRecoveryAsync(client, token).ConfigureAwait(false);
                    await _core.ReplaceHeartbeatAsync(client).ConfigureAwait(false);

                    RpcClientRuntime? previous;
                    lock (_stateGate)
                    {
                        if (_disposal is not null)
                            throw new OperationCanceledException("Lakona game client is being disposed.", token);
                        token.ThrowIfCancellationRequested();
                        if (disconnected || client.ConnectionState != RpcClientConnectionState.Connected)
                            throw new InvalidOperationException("RPC connection closed before becoming ready.");
                        previous = _rpcClient;
                        _rpcClient = client;
                        _dispatcher.SetCurrent(client);
                        if (recovering) _core.MarkRecovered();
                        else _core.MarkReady();
                        _apiReady = true;
                        SetConnectionState(LakonaGameConnectionState.Connected);
                    }
                    if (previous is not null)
                        await previous.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                    await client.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }
            finally
            {
                _connectionGate.Release();
            }
        }

        private void BindFrameworkNotifications(RpcClientRuntime client)
        {
            client.RegisterRawNotificationHandler(GameSessionNotificationRpcIds.ServiceId,
                GameSessionNotificationRpcIds.EstablishedNotificationId, async payload =>
                {
                    await _core.ApplyGameSessionEstablishedAsync(LakonaInternalCodec.DecodeGameSessionEstablished(payload))
                        .ConfigureAwait(false);
                    using var acknowledgement = await client.CallRawAsync(GameSessionEstablishedRpcIds.ServiceId,
                        GameSessionEstablishedRpcIds.AckMethodId, ReadOnlyMemory<byte>.Empty, _lifetime.Token)
                        .ConfigureAwait(false);
                });
            client.RegisterRawNotificationHandler(GameSessionNotificationRpcIds.ServiceId,
                GameSessionNotificationRpcIds.TerminatedNotificationId, payload =>
                {
                    _core.ApplySessionTerminationNotice(LakonaInternalCodec.DecodeSessionTerminationNotice(payload));
                    return default;
                });
        }

        // Called under _stateGate; only the current generation may initiate recovery.
        private void HandleDisconnected(RpcClientRuntime source, Exception? ex)
        {
            if (_disposal is not null || !ReferenceEquals(_rpcClient, source))
                return;
            _apiReady = false;
            SetConnectionState(LakonaGameConnectionState.Reconnecting);
            _dispatcher.ClearCurrent(source);
            _core.MarkReconnecting();
            if (_recoveryTask is null || _recoveryTask.IsCompleted)
                _recoveryTask = RecoverAsync(ex);
        }

        private async Task RecoverAsync(Exception? disconnectReason)
        {
            var scheduler = _options.RecoveryScheduler;
            var deadline = scheduler.GetUtcNow() + _core.SessionResumeWindow;
            var attempt = 0;
            while (scheduler.GetUtcNow() < deadline && !_lifetime.IsCancellationRequested)
            {
                try
                {
                    var remaining = deadline - scheduler.GetUtcNow();
                    var delay = scheduler.GetDelay(attempt++);
                    if (delay > remaining) delay = remaining;
                    await scheduler.DelayAsync(delay, _lifetime.Token).ConfigureAwait(false);
                    await ConnectGenerationAsync(true, _lifetime.Token).ConfigureAwait(false);
                    return;
                }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
                {
                    return;
                }
                catch (GameSessionRecoveryRejectedException)
                {
                    ReportRecoveryFailure(disconnectReason);
                    return;
                }
                catch
                {
                }
            }
            if (_lifetime.IsCancellationRequested) return;
            _core.ApplyRecoveryRejection(new GameSessionRecoveryHandshakeResult
            {
                Status = GameSessionRecoveryStatus.StateLost,
                Reason = "Game session recovery window expired."
            });
            ReportRecoveryFailure(disconnectReason);
        }

        private void ReportRecoveryFailure(Exception? disconnectReason)
        {
            lock (_stateGate)
            {
                if (_disposal is not null) return;
                SetConnectionState(LakonaGameConnectionState.Disconnected);
            }
            Disconnected?.Invoke(disconnectReason);
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
