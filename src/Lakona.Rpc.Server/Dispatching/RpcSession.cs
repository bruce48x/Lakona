using System.Buffers;
using System.ComponentModel;
using System.Diagnostics;
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
    internal delegate ValueTask<RpcResponseEnvelope> RpcHandler(RpcRequestEnvelope req, CancellationToken ct);

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
    internal sealed class RpcSession : IAsyncDisposable
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<(int serviceId, int methodId), RpcHandler> _handlers = new();
        private readonly TrackedTaskCollection _inflightRequests = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, Lazy<object>> _scopedServices = new();
        private readonly RpcConnectionChannel _connection;
        private readonly ServerRequestDispatcher _requestDispatcher;
        private readonly ITransport _transport;
        private readonly IRpcSerializer _serializer;
        private readonly RpcKeepAliveOptions _keepAlive;
        private readonly RpcServerLimits _limits;
        private readonly ILogger _logger;
        private readonly ILogger _requestLogger;
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
        ///     Creates a session with an explicit connection id.
        /// </summary>
        /// <param name="transport">Transport for this connection.</param>
        /// <param name="serializer">Serializer used for RPC payloads.</param>
        /// <param name="connectionId">Stable connection id used in logs and scoped services.</param>
        public RpcSession(ITransport transport, IRpcSerializer serializer, string connectionId)
            : this(transport, serializer, registry: null, connectionId, false, keepAlive: null)
        {
        }

        /// <summary>
        ///     Creates a session with an explicit connection id and transport ownership setting.
        /// </summary>
        public RpcSession(ITransport transport, IRpcSerializer serializer, string connectionId, bool ownsTransport)
            : this(transport, serializer, registry: null, connectionId, ownsTransport, keepAlive: null)
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
        ///     Creates a session backed by a service registry with an explicit connection id.
        /// </summary>
        public RpcSession(ITransport transport, IRpcSerializer serializer, RpcServiceRegistry registry, string connectionId)
            : this(transport, serializer, registry, connectionId, false, keepAlive: null)
        {
        }

        /// <summary>
        ///     Creates a fully configured session.
        /// </summary>
        /// <param name="transport">Transport for this connection.</param>
        /// <param name="serializer">Serializer used for RPC payloads.</param>
        /// <param name="registry">Optional generated service registry.</param>
        /// <param name="connectionId">Stable connection id used in logs and scoped services.</param>
        /// <param name="ownsTransport">Whether disposing the session also disposes the transport.</param>
        /// <param name="keepAlive">Optional keepalive configuration.</param>
        /// <param name="logger">Optional host/session logger.</param>
        /// <param name="requestLogger">Optional request and notification logger.</param>
        /// <param name="limits">Optional request concurrency and queue limits.</param>
        /// <param name="requestGates">Optional per-session request admission gates.</param>
        /// <param name="remoteEndPoint">Optional endpoint supplied by the connection acceptor.</param>
        public RpcSession(
            ITransport transport,
            IRpcSerializer serializer,
            RpcServiceRegistry? registry,
            string connectionId,
            bool ownsTransport,
            RpcKeepAliveOptions? keepAlive = null,
            ILogger? logger = null,
            ILogger? requestLogger = null,
            RpcServerLimits? limits = null,
            IReadOnlyList<IRpcSessionRequestGate>? requestGates = null,
            EndPoint? remoteEndPoint = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _ownsTransport = ownsTransport;
            _keepAlive = keepAlive ?? RpcKeepAliveOptions.Disabled;
            _logger = logger ?? DefaultRpcLogging.CreateLogger<RpcSession>();
            _requestLogger = requestLogger
                ?? logger
                ?? DefaultRpcLogging.CreateLogger(RpcServerRequestLogging.Category);
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
            _connection = new RpcConnectionChannel(_transport, _keepAlive);
            _requestDispatcher = new ServerRequestDispatcher(_handlers, registry, requestGates, _connection, _requestLogger);
            ConnectionId = connectionId ?? throw new ArgumentNullException(nameof(connectionId));
            RemoteEndPoint = remoteEndPoint ?? ResolveRemoteEndPoint(_transport);
        }

        /// <summary>
        ///     Unique identifier for this connection session.
        /// </summary>
        public string ConnectionId { get; }

        internal RpcConnectionInfo ConnectionInfo => new(ConnectionId, RemoteEndPoint);

        internal void LogInvalidRequestPayload(RpcRequestFrame request, Exception exception)
        {
            _requestLogger.LogWarning(
                exception,
                "RPC request payload could not be deserialized for request {RequestId} service {ServiceId} method {MethodId} in connection {ConnectionId}; payload length {PayloadLength}; exception {ExceptionType}.",
                request.RequestId,
                request.ServiceId,
                request.MethodId,
                ConnectionId,
                request.Payload.Length,
                exception.GetType().Name);
        }

        /// <summary>
        ///     Remote endpoint of the connected client, if the underlying transport supports it.
        /// </summary>
        public EndPoint? RemoteEndPoint { get; private set; }

        public string? RemoteAddress => (RemoteEndPoint as IPEndPoint)?.Address.ToString();

        public int? RemotePort => (RemoteEndPoint as IPEndPoint)?.Port;

        public IRpcSerializer Serializer => _serializer;

        /// <summary>
        ///     Last UTC timestamp at which this session sent a frame.
        /// </summary>
        public DateTimeOffset LastSendAt => _connection.LastSendAt;

        /// <summary>
        ///     Last UTC timestamp at which this session received a frame.
        /// </summary>
        public DateTimeOffset LastReceiveAt => _connection.LastReceiveAt;

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

            var activation = _scopedServices.GetOrAdd(
                serviceId,
                _ => new Lazy<object>(
                    () => factory(this)
                        ?? throw new InvalidOperationException($"Service factory returned null for service id {serviceId}."),
                    LazyThreadSafetyMode.ExecutionAndPublication));

            return (TService)activation.Value;
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
            await SendNotificationAsync(serviceId, methodId, arg, metadata: null, ct).ConfigureAwait(false);
        }

        public async ValueTask SendNotificationAsync<TArg>(
            int serviceId,
            int methodId,
            TArg arg,
            RpcPushMetadata? metadata,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();
            using var writer = RpcEnvelopeCodec.BeginPushPayload(
                serviceId,
                methodId,
                metadata);
            if (arg is not null)
            {
                _serializer.Serialize(writer, arg);
            }

            var payloadLength = writer.PayloadLength;
            using var bytes = RpcEnvelopeCodec.CompletePayload(writer);
            LogNotificationSent(serviceId, methodId, payloadLength);
            await SendFrameAsyncSerialized(bytes.Memory, ct).ConfigureAwait(false);
        }

        public async ValueTask SendRawNotificationAsync(
            int serviceId,
            int methodId,
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken = default)
        {
            await SendRawNotificationAsync(
                serviceId,
                methodId,
                payload,
                metadata: null,
                cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask SendRawNotificationAsync(
            int serviceId,
            int methodId,
            ReadOnlyMemory<byte> payload,
            RpcPushMetadata? metadata,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            using var writer = RpcEnvelopeCodec.BeginPushPayload(
                serviceId,
                methodId,
                metadata);
            writer.Write(payload.Span);
            using var bytes = RpcEnvelopeCodec.CompletePayload(writer);
            LogNotificationSent(serviceId, methodId, payload.Length);
            await SendFrameAsyncSerialized(bytes.Memory, cancellationToken).ConfigureAwait(false);
        }

        private void LogNotificationSent(int serviceId, int methodId, int payloadBytes)
        {
            _requestLogger.LogDebug(
                "RPC notification sent service {ServiceId} method {MethodId} payloadBytes {PayloadBytes} in connection {ConnectionId}.",
                serviceId,
                methodId,
                payloadBytes,
                ConnectionId);
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
                _connection.ResetActivity();
                RemoteEndPoint ??= ResolveRemoteEndPoint(_transport);
                _cts = new CancellationTokenSource();
                var serverCts = _cts;
                _loop = LoopAsync(serverCts);
                if (_keepAlive.Enabled)
                    _keepAliveLoop = KeepAliveLoopAsync(serverCts);
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
                        frame = await _connection.ReceiveApplicationFrameAsync(ct).ConfigureAwait(false);
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

                        var frameType = RpcEnvelopeCodec.PeekFrameType(frame.Span);
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
                await DisposeScopedServicesAsync().ConfigureAwait(false);
                ResetRuntimeState(serverCts);
                Disconnected?.Invoke(disconnectError);
            }
        }

        private async Task KeepAliveLoopAsync(CancellationTokenSource? serverCts)
        {
            if (serverCts is null)
                return;

            await _connection.RunKeepAliveAsync(
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
                serverCts.Token).ConfigureAwait(false);
        }

        private void EnqueueRequestProcessing(RpcRequestFrame req, CancellationToken ct)
        {
            if (!_requestBudget.Wait(0))
            {
                _requestLogger.LogWarning(
                    "RPC request rejected {RequestId} status {Status} service {ServiceId} method {MethodId} in connection {ConnectionId}. {ErrorMessage}",
                    req.RequestId,
                    RpcStatus.Overloaded,
                    req.ServiceId,
                    req.MethodId,
                    ConnectionId,
                    "RPC server is overloaded; request queue is full.");
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
            var startedAt = Stopwatch.GetTimestamp();
            try
            {
                await _requestConcurrencyGate.WaitAsync(ct).ConfigureAwait(false);
                enteredConcurrencyGate = true;

                await _requestDispatcher.DispatchAsync(this, req, ct, startedAt).ConfigureAwait(false);
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
            await _connection.SendAsync(frame, ct).ConfigureAwait(false);
        }

        private void ResetRuntimeState(CancellationTokenSource serverCts)
        {
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
            await DisposeScopedServicesAsync().ConfigureAwait(false);

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
        }

        private async ValueTask DisposeScopedServicesAsync()
        {
            foreach (var entry in _scopedServices)
            {
                if (!_scopedServices.TryRemove(entry.Key, out var activation) || !activation.IsValueCreated)
                    continue;

                object service;
                try
                {
                    service = activation.Value;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to resolve RPC service {ServiceId} while releasing connection {ConnectionId}.", entry.Key, ConnectionId);
                    continue;
                }

                try
                {
                    if (service is IAsyncDisposable asyncDisposable)
                        await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                    else if (service is IDisposable disposable)
                        disposable.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to release RPC service {ServiceId} for connection {ConnectionId}.", entry.Key, ConnectionId);
                }
            }
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
            _connection.Dispose();
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

        private static EndPoint? ResolveRemoteEndPoint(ITransport transport)
        {
            return (transport as IRemoteEndPointProvider)?.RemoteEndPoint;
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
