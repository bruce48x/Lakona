using System.ComponentModel;
using System.Linq;
using System.Net;
using Microsoft.Extensions.Logging;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Server
{
    /// <summary>
    ///     Low-level handler for a decoded RPC request.
    /// </summary>
    /// <param name="req">Request envelope.</param>
    /// <param name="ct">Cancellation token for request processing.</param>
    /// <returns>Response envelope to send back to the client.</returns>
    /// <remarks>
    ///     Runtime-internal handler wiring. Regular applications should define RPC contracts and service
    ///     implementations, then let generated binders register handlers.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public delegate ValueTask<RpcResponseEnvelope> RpcHandler(RpcRequestEnvelope req, CancellationToken ct);

    /// <summary>
    ///     Runtime for one accepted client connection.
    /// </summary>
    /// <remarks>
    ///     A session owns receive, dispatch, optional keepalive, and server push for one transport connection.
    ///     Generated server binders usually create session-scoped service instances through
    ///     <see cref="GetOrAddScopedService{TService}"/>. Regular server applications should use
    ///     <see cref="RpcServerHostBuilder"/> instead of constructing sessions directly.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed class RpcSession : IAsyncDisposable
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<(int serviceId, int methodId), RpcHandler> _handlers = new();
        private readonly TrackedTaskCollection _inflightRequests = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, object> _scopedServices = new();
        private readonly RpcKeepAliveState _keepAliveState;
        private readonly SerializedFrameSender _sender;
        private readonly ServerRequestDispatcher _requestDispatcher;
        private readonly ITransport _transport;
        private readonly IRpcSerializer _serializer;
        private readonly RpcKeepAliveOptions _keepAlive;
        private readonly RpcServerLimits _limits;
        private readonly ILogger _logger;
        private readonly bool _ownsTransport;
        private readonly SemaphoreSlim _requestConcurrencyGate;
        private readonly SemaphoreSlim _requestBudget;

        private CancellationTokenSource? _cts;
        private Task? _keepAliveLoop;
        private Task? _loop;
        private int _disposed;
        private int _started;
        private int _terminated;
        private int _transportDisposed;
        private long _disconnectReasonSet;
        private Exception? _disconnectReason;

        /// <summary>
        ///     Creates a session that does not own the transport.
        /// </summary>
        /// <param name="transport">Transport for this connection.</param>
        /// <param name="serializer">Serializer used for RPC payloads.</param>
        public RpcSession(ITransport transport, IRpcSerializer serializer)
            : this(transport, serializer, registry: null, Guid.NewGuid().ToString("N"), false, keepAlive: null)
        {
        }

        /// <summary>
        ///     Creates a session and optionally disposes the transport when the session is disposed.
        /// </summary>
        /// <param name="transport">Transport for this connection.</param>
        /// <param name="serializer">Serializer used for RPC payloads.</param>
        /// <param name="ownsTransport">Whether disposing the session also disposes the transport.</param>
        public RpcSession(ITransport transport, IRpcSerializer serializer, bool ownsTransport)
            : this(transport, serializer, registry: null, Guid.NewGuid().ToString("N"), ownsTransport, keepAlive: null)
        {
        }

        /// <summary>
        ///     Creates a session with an explicit context id.
        /// </summary>
        /// <param name="transport">Transport for this connection.</param>
        /// <param name="serializer">Serializer used for RPC payloads.</param>
        /// <param name="contextId">Stable session id used in logs and scoped services.</param>
        public RpcSession(ITransport transport, IRpcSerializer serializer, string contextId)
            : this(transport, serializer, registry: null, contextId, false, keepAlive: null)
        {
        }

        /// <summary>
        ///     Creates a session with an explicit context id and transport ownership setting.
        /// </summary>
        public RpcSession(ITransport transport, IRpcSerializer serializer, string contextId, bool ownsTransport)
            : this(transport, serializer, registry: null, contextId, ownsTransport, keepAlive: null)
        {
        }

        /// <summary>
        ///     Creates a session backed by a service registry.
        /// </summary>
        public RpcSession(ITransport transport, IRpcSerializer serializer, RpcServiceRegistry registry)
            : this(transport, serializer, registry, Guid.NewGuid().ToString("N"), false, keepAlive: null)
        {
        }

        /// <summary>
        ///     Creates a session backed by a service registry and optional transport ownership.
        /// </summary>
        public RpcSession(ITransport transport, IRpcSerializer serializer, RpcServiceRegistry registry, bool ownsTransport)
            : this(transport, serializer, registry, Guid.NewGuid().ToString("N"), ownsTransport, keepAlive: null)
        {
        }

        /// <summary>
        ///     Creates a session backed by a service registry with an explicit context id.
        /// </summary>
        public RpcSession(ITransport transport, IRpcSerializer serializer, RpcServiceRegistry registry, string contextId)
            : this(transport, serializer, registry, contextId, false, keepAlive: null)
        {
        }

        /// <summary>
        ///     Creates a fully configured session.
        /// </summary>
        /// <param name="transport">Transport for this connection.</param>
        /// <param name="serializer">Serializer used for RPC payloads.</param>
        /// <param name="registry">Optional generated service registry.</param>
        /// <param name="contextId">Stable session id used in logs and scoped services.</param>
        /// <param name="ownsTransport">Whether disposing the session also disposes the transport.</param>
        /// <param name="keepAlive">Optional keepalive configuration.</param>
        /// <param name="logger">Optional logger.</param>
        /// <param name="limits">Optional request concurrency and queue limits.</param>
        public RpcSession(
            ITransport transport,
            IRpcSerializer serializer,
            RpcServiceRegistry? registry,
            string contextId,
            bool ownsTransport,
            RpcKeepAliveOptions? keepAlive = null,
            ILogger? logger = null,
            RpcServerLimits? limits = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _ownsTransport = ownsTransport;
            _keepAlive = keepAlive ?? RpcKeepAliveOptions.Disabled;
            _logger = logger ?? DefaultRpcLogging.CreateLogger<RpcSession>();
            _limits = limits?.Clone() ?? new RpcServerLimits();
            _limits.Validate();
            _requestConcurrencyGate = new SemaphoreSlim(
                _limits.MaxConcurrentRequestsPerSession,
                _limits.MaxConcurrentRequestsPerSession);
            checked
            {
                var requestBudget = _limits.MaxConcurrentRequestsPerSession + _limits.MaxQueuedRequestsPerSession;
                _requestBudget = new SemaphoreSlim(requestBudget, requestBudget);
            }
            _keepAliveState = new RpcKeepAliveState(_keepAlive.MeasureRtt);
            _sender = new SerializedFrameSender(_transport, _keepAliveState);
            _requestDispatcher = new ServerRequestDispatcher(_handlers, registry, _sender, _logger);
            ContextId = contextId ?? throw new ArgumentNullException(nameof(contextId));
            RemoteEndPoint = ResolveRemoteEndPoint(_transport);
        }

        /// <summary>
        ///     Unique identifier for this connection session.
        /// </summary>
        public string ContextId { get; }

        /// <summary>
        ///     Remote endpoint of the connected client, if the underlying transport supports it.
        /// </summary>
        public IPEndPoint? RemoteEndPoint { get; private set; }

        public string? RemoteAddress => RemoteEndPoint?.Address.ToString();

        public int? RemotePort => RemoteEndPoint?.Port;

        public IRpcSerializer Serializer => _serializer;

        /// <summary>
        ///     Last UTC timestamp at which this session sent a frame.
        /// </summary>
        public DateTimeOffset LastSendAt => _keepAliveState.LastSendAt;

        /// <summary>
        ///     Last UTC timestamp at which this session received a frame.
        /// </summary>
        public DateTimeOffset LastReceiveAt => _keepAliveState.LastReceiveAt;

        /// <summary>
        ///     Raised when the session receive loop ends.
        /// </summary>
        public event Action<Exception?>? Disconnected;

        /// <summary>
        ///     Registers a low-level request handler for one service method.
        /// </summary>
        /// <param name="serviceId">Stable service id.</param>
        /// <param name="methodId">Stable method id.</param>
        /// <param name="handler">Request handler.</param>
        public void Register(int serviceId, int methodId, RpcHandler handler)
        {
            ThrowIfDisposed();
            if (handler is null) throw new ArgumentNullException(nameof(handler));
            _handlers[(serviceId, methodId)] = handler;
        }

        /// <summary>
        ///     Gets or creates a service instance scoped to this session and service id.
        /// </summary>
        /// <typeparam name="TService">Service implementation type.</typeparam>
        /// <param name="serviceId">Stable service id.</param>
        /// <param name="factory">Factory invoked once per session and service id.</param>
        /// <returns>The existing or newly created service instance.</returns>
        public TService GetOrAddScopedService<TService>(int serviceId, Func<RpcSession, TService> factory)
            where TService : class
        {
            ThrowIfDisposed();
            if (factory is null) throw new ArgumentNullException(nameof(factory));

            var service = _scopedServices.GetOrAdd(serviceId, _ =>
                factory(this) ?? throw new InvalidOperationException($"Service factory returned null for service id {serviceId}."));

            return (TService)service;
        }

        /// <summary>
        ///     Sends a server-to-client notification.
        /// </summary>
        /// <typeparam name="TArg">Notification DTO type.</typeparam>
        /// <param name="serviceId">Stable service id.</param>
        /// <param name="methodId">Stable notification method id.</param>
        /// <param name="arg">Notification DTO instance.</param>
        /// <param name="ct">Cancellation token for the send operation.</param>
        public async ValueTask SendNotificationAsync<TArg>(int serviceId, int methodId, TArg arg, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            using var payload = arg is null ? TransportFrame.Empty : _serializer.SerializeFrame(arg);
            var push = new RpcPushEnvelope
            {
                ServiceId = serviceId,
                MethodId = methodId,
                Payload = payload.Memory
            };
            using var bytes = RpcEnvelopeCodec.EncodePush(push);
            await SendFrameAsyncSerialized(bytes.Memory, ct).ConfigureAwait(false);
        }

        /// <summary>
        ///     Connects the transport and starts the session receive loop.
        /// </summary>
        /// <param name="ct">Cancellation token for the initial transport connection.</param>
        /// <exception cref="InvalidOperationException">Thrown when the session has already been started.</exception>
        public async ValueTask StartAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (Volatile.Read(ref _terminated) != 0)
                throw new InvalidOperationException("RpcSession cannot be restarted after it has stopped.");

            if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
                throw new InvalidOperationException("RpcSession already started.");

            try
            {
                await _transport.ConnectAsync(ct).ConfigureAwait(false);
                _keepAliveState.MarkSent();
                _keepAliveState.MarkReceived();
                RemoteEndPoint ??= ResolveRemoteEndPoint(_transport);
                _cts = new CancellationTokenSource();
                var serverCts = _cts;
                _loop = Task.Run(() => LoopAsync(serverCts));
                if (_keepAlive.Enabled)
                    _keepAliveLoop = Task.Run(() => KeepAliveLoopAsync(serverCts));
            }
            catch
            {
                if (_cts is not null)
                {
                    _cts.Dispose();
                    _cts = null;
                }
                _loop = null;
                Interlocked.Exchange(ref _started, 0);
                throw;
            }
        }

        /// <summary>
        ///     Waits until the session receive loop and in-flight requests complete.
        /// </summary>
        public async ValueTask WaitForCompletionAsync()
        {
            if (_loop is null)
                return;

            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }

            await _inflightRequests.WaitAsync().ConfigureAwait(false);
        }

        /// <summary>
        ///     Starts the session, waits for completion, and stops it in a finally block.
        /// </summary>
        /// <param name="ct">Cancellation token linked to the session loop.</param>
        public async ValueTask RunAsync(CancellationToken ct = default)
        {
            await StartAsync(ct).ConfigureAwait(false);

            // StartAsync creates a fresh internal CancellationTokenSource unlinked from ct.
            // Register a callback so that cancelling ct also cancels the internal session loop.
            using var externalCancellation = ct.Register(() =>
            {
                var cts = _cts;
                if (cts is not null)
                    try { cts.Cancel(); } catch (ObjectDisposedException) { }
            });

            try
            {
                await WaitForCompletionAsync().ConfigureAwait(false);
            }
            finally
            {
                await StopAsync().ConfigureAwait(false);
            }
        }

        private async Task LoopAsync(CancellationTokenSource? serverCts)
        {
            if (serverCts is null) return;

            var ct = serverCts.Token;
            Exception? disconnectError = null;
            var cancelInflightRequests = false;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    TransportFrame frame;
                    try
                    {
                        frame = await _transport.ReceiveFrameAsync(ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        cancelInflightRequests = true;
                        break;
                    }
                    catch (InvalidOperationException) when (!_transport.IsConnected)
                    {
                        cancelInflightRequests = true;
                        break;
                    }

                    using (frame)
                    {
                        if (frame.Length == 0)
                        {
                            cancelInflightRequests = true;
                            break;
                        }

                        _keepAliveState.MarkReceived();
                        var frameType = RpcEnvelopeCodec.PeekFrameType(frame.Span);
                        if (frameType == RpcFrameType.KeepAlivePing)
                        {
                            var ping = RpcEnvelopeCodec.DecodeKeepAlivePing(frame.Span);
                            using var pongBytes = RpcEnvelopeCodec.EncodeKeepAlivePong(new RpcKeepAlivePongEnvelope
                            {
                                TimestampTicksUtc = ping.TimestampTicksUtc
                            });
                            await SendFrameAsyncSerialized(pongBytes.Memory, ct).ConfigureAwait(false);
                            continue;
                        }

                        if (frameType != RpcFrameType.Request)
                            continue;

                        var req = RpcEnvelopeCodec.DecodeRequest(frame);
                        EnqueueRequestProcessing(req, ct);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    cancelInflightRequests = true;
                    disconnectError = ex;
                }
            }
            finally
            {
                if (cancelInflightRequests)
                    CancelSessionLoop(serverCts);

                if (disconnectError is null)
                    disconnectError = _disconnectReason;
                await _inflightRequests.WaitAsync().ConfigureAwait(false);
                ResetRuntimeState(serverCts);
                Disconnected?.Invoke(disconnectError);
            }
        }

        private async Task KeepAliveLoopAsync(CancellationTokenSource? serverCts)
        {
            if (serverCts is null)
                return;

            var coordinator = new RpcKeepAliveCoordinator(
                _transport,
                _sender,
                _keepAliveState,
                _keepAlive,
                "RPC session keepalive timed out.",
                ex =>
                {
                    SetDisconnectReason(ex);
                    try
                    {
                        serverCts.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                },
                markTimedOut: false);

            await coordinator.RunAsync(serverCts.Token).ConfigureAwait(false);
        }

        private void EnqueueRequestProcessing(RpcRequestFrame req, CancellationToken ct)
        {
            if (!_requestBudget.Wait(0))
            {
                _logger.LogWarning(
                    "[{ContextId}] Rejected request {RequestId} for service {ServiceId} method {MethodId} because the session request queue is full.",
                    ContextId,
                    req.RequestId,
                    req.ServiceId,
                    req.MethodId);
                var requestId = req.RequestId;
                req.Dispose();
                _inflightRequests.Track(SendOverloadedResponseAsync(requestId, ct));
                return;
            }

            var task = ProcessRequestAsync(req, ct);
            _inflightRequests.Track(task);
        }

        private async Task ProcessRequestAsync(RpcRequestFrame req, CancellationToken ct)
        {
            var enteredConcurrencyGate = false;
            try
            {
                await _requestConcurrencyGate.WaitAsync(ct).ConfigureAwait(false);
                enteredConcurrencyGate = true;

                await _requestDispatcher.DispatchAsync(this, req, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException) when (!_transport.IsConnected)
            {
            }
            finally
            {
                req.Dispose();
                if (enteredConcurrencyGate)
                    _requestConcurrencyGate.Release();

                _requestBudget.Release();
            }
        }

        private async Task SendOverloadedResponseAsync(uint requestId, CancellationToken ct)
        {
            try
            {
                await _requestDispatcher.SendOverloadedResponseAsync(requestId, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException) when (!_transport.IsConnected)
            {
            }
        }

        private async ValueTask SendFrameAsyncSerialized(ReadOnlyMemory<byte> frame, CancellationToken ct)
        {
            await _sender.SendAsync(frame, ct).ConfigureAwait(false);
        }

        private void ResetRuntimeState(CancellationTokenSource serverCts)
        {
            _scopedServices.Clear();

            if (ReferenceEquals(_cts, serverCts))
            {
                _cts = null;
                try
                {
                    serverCts.Dispose();
                }
                catch (ObjectDisposedException)
                {
                }
            }

            _loop = null;
            _keepAliveLoop = null;
            Interlocked.Exchange(ref _started, 0);
        }

        /// <summary>
        ///     Requests session shutdown and waits for in-flight requests to complete.
        /// </summary>
        public async ValueTask StopAsync()
        {
            var cts = _cts;
            var loop = _loop;
            var keepAliveLoop = _keepAliveLoop;

            if (cts is not null)
                try
                {
                    cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }

            if (loop is not null)
                try
                {
                    await loop.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (ObjectDisposedException)
                {
                }

            if (keepAliveLoop is not null)
                try
                {
                    await keepAliveLoop.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (ObjectDisposedException)
                {
                }

            await _inflightRequests.WaitAsync().ConfigureAwait(false);

            if (cts is not null && ReferenceEquals(_cts, cts))
            {
                _cts = null;
                try
                {
                    cts.Dispose();
                }
                catch (ObjectDisposedException)
                {
                }
            }

            _loop = null;
            _keepAliveLoop = null;
            Interlocked.Exchange(ref _started, 0);
            Interlocked.Exchange(ref _terminated, 1);
            _scopedServices.Clear();
        }

        /// <summary>
        ///     Stops the session and disposes owned resources.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            await StopAsync().ConfigureAwait(false);
            await DisposeOwnedTransportIfNeededAsync().ConfigureAwait(false);
            _requestConcurrencyGate.Dispose();
            _requestBudget.Dispose();
            _sender.Dispose();
        }

        private async ValueTask DisposeOwnedTransportIfNeededAsync()
        {
            if (!_ownsTransport)
                return;

            if (Interlocked.Exchange(ref _transportDisposed, 1) != 0)
                return;

            await _transport.DisposeAsync().ConfigureAwait(false);
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0)
                throw new ObjectDisposedException(nameof(RpcSession));
        }

        private static IPEndPoint? ResolveRemoteEndPoint(ITransport transport)
        {
            return (transport as IRemoteEndPointProvider)?.RemoteEndPoint as IPEndPoint;
        }

        private void SetDisconnectReason(Exception ex)
        {
            if (Interlocked.CompareExchange(ref _disconnectReasonSet, 1, 0) == 0)
                _disconnectReason = ex;
        }

        private static void CancelSessionLoop(CancellationTokenSource serverCts)
        {
            if (serverCts.IsCancellationRequested)
                return;

            try
            {
                serverCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }
}
