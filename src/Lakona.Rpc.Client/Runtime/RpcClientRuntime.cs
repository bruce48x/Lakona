using System.Buffers;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Lakona.Rpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lakona.Rpc.Client
{
    /// <summary>
    ///     Handles a serialized server-to-client notification payload.
    /// </summary>
    /// <param name="payload">Serialized notification payload.</param>
    public delegate ValueTask RpcNotificationPayloadHandler(ReadOnlyMemory<byte> payload);

    /// <summary>
    ///     Wraps server-to-client notification dispatch with optional push metadata processing.
    /// </summary>
    /// <param name="metadata">Optional generic push metadata carried by the frame.</param>
    /// <param name="next">Callback that dispatches the notification to the registered handler.</param>
    public delegate ValueTask RpcNotificationDispatchMiddleware(RpcPushMetadata? metadata, Func<ValueTask> next);

    /// <summary>
    ///     Default client runtime for Lakona.Rpc request/response calls and server notification dispatch.
    /// </summary>
    /// <remarks>
    ///     The runtime owns background receive, notification, and keepalive loops after <see cref="StartAsync"/>.
    ///     Notification handlers run on the runtime notification loop and are not marshalled to the Unity main thread.
    /// </remarks>
    public sealed class RpcClientRuntime : IAsyncDisposable, IRpcClient
    {
        private const long NotificationQueueCountWarningThreshold = 256;
        private const long NotificationQueueBytesWarningThreshold = 1024 * 1024;

        private readonly CancellationTokenSource _cts = new();
        private readonly RpcConnectionChannel _connection;
        private readonly RpcPendingRequestCollection _pending = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<(int serviceId, int methodId), RegisteredNotificationHandler> _notificationHandlers = new();
        private readonly Channel<RpcPushFrame> _pushQueue = Channel.CreateUnbounded<RpcPushFrame>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });
        private readonly ITransport _transport;
        private readonly IRpcSerializer _serializer;
        private readonly RpcKeepAliveOptions _keepAlive;
        private readonly ILogger _requestLogger;
        private RpcNotificationDispatchMiddleware? _notificationDispatchMiddleware;
        private int _disposed;
        private int _nextId;
        private int _started;
        private long _disconnectReasonSet;
        private long _nextNotificationQueueBytesWarning =
            NotificationQueueBytesWarningThreshold;
        private long _nextNotificationQueueCountWarning =
            NotificationQueueCountWarningThreshold;
        private long _queuedNotificationBytes;
        private long _queuedNotificationCount;

        private Task? _recvLoop;
        private Task? _keepAliveLoop;
        private Task? _pushLoop;
        private Exception? _disconnectReason;

        /// <summary>
        ///     Creates a runtime from client options.
        /// </summary>
        /// <param name="options">Client options containing transport, serializer, keepalive, and security settings.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
        public RpcClientRuntime(RpcClientOptions options)
            : this(
                (options ?? throw new ArgumentNullException(nameof(options))).CreateConfiguredTransport(),
                options.Serializer,
                options.KeepAlive,
                options.LoggerFactory)
        {
        }

        /// <summary>
        ///     Creates a runtime from explicit transport and serializer instances.
        /// </summary>
        /// <param name="transport">Connected or connectable transport used by the runtime.</param>
        /// <param name="serializer">Serializer used for RPC payloads.</param>
        /// <param name="keepAlive">Optional keepalive configuration.</param>
        /// <param name="loggerFactory">Optional logger factory for framework request and notification logs.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="transport"/> or <paramref name="serializer"/> is null.</exception>
        public RpcClientRuntime(
            ITransport transport,
            IRpcSerializer serializer,
            RpcKeepAliveOptions? keepAlive = null,
            ILoggerFactory? loggerFactory = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _keepAlive = keepAlive ?? RpcKeepAliveOptions.Disabled;
            _requestLogger = loggerFactory?.CreateLogger(RpcClientRequestLogging.Category)
                ?? NullLogger.Instance;
            _connection = new RpcConnectionChannel(_transport, _keepAlive);
        }

        /// <summary>
        ///     Raised when the receive loop ends.
        /// </summary>
        /// <remarks>
        ///     The event argument is the disconnect reason when one is available. A null value means a normal or
        ///     locally requested shutdown.
        /// </remarks>
        public event Action<Exception?>? Disconnected;

        /// <summary>
        ///     Raised when a server-to-client notification frame has no registered handler.
        /// </summary>
        public event Action<RpcUnhandledNotificationContext>? UnhandledNotificationReceived;

        /// <summary>
        ///     Raised when a registered notification handler throws.
        /// </summary>
        public event Action<RpcNotificationHandlerExceptionContext>? NotificationHandlerException;

        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public void SetNotificationDispatchMiddleware(RpcNotificationDispatchMiddleware? middleware)
        {
            ThrowIfDisposed();
            _notificationDispatchMiddleware = middleware;
        }

        /// <summary>
        ///     Last UTC timestamp at which the runtime sent a frame.
        /// </summary>
        public DateTimeOffset LastSendAt => _connection.LastSendAt;

        /// <summary>
        ///     Last UTC timestamp at which the runtime received a frame.
        /// </summary>
        public DateTimeOffset LastReceiveAt => _connection.LastReceiveAt;

        /// <summary>
        ///     Last measured keepalive round-trip time, when RTT measurement is enabled.
        /// </summary>
        public TimeSpan? LastRtt => _connection.LastRtt;

        /// <summary>
        ///     Indicates whether the runtime stopped because keepalive timed out.
        /// </summary>
        public bool TimedOutByKeepAlive => _connection.TimedOut;

        /// <summary>
        ///     Connects the transport and starts background runtime loops.
        /// </summary>
        /// <param name="ct">Cancellation token for the initial transport connection.</param>
        /// <exception cref="InvalidOperationException">Thrown when the runtime has already been started.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when the runtime has been disposed.</exception>
        public async ValueTask StartAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
                throw new InvalidOperationException("RpcClient already started.");

            try
            {
                await _transport.ConnectAsync(ct);
                _connection.ResetActivity();
                _pushLoop = Task.Run(ProcessPushLoopAsync);
                _recvLoop = Task.Run(ReceiveLoopAsync);
                if (_keepAlive.Enabled)
                    _keepAliveLoop = Task.Run(KeepAliveLoopAsync);
            }
            catch
            {
                Interlocked.Exchange(ref _started, 0);
                throw;
            }
        }

        /// <inheritdoc />
        public void RegisterNotificationHandler<TArg>(RpcNotificationMethod<TArg> method, Func<TArg, ValueTask> handler)
        {
            ThrowIfDisposed();
            if (handler is null) throw new ArgumentNullException(nameof(handler));
            var registered = _notificationHandlers.TryAdd((method.ServiceId, method.MethodId), new RegisteredNotificationHandler(typeof(TArg), payload =>
            {
                if (typeof(TArg) == typeof(RpcVoid))
                {
                    return handler((TArg)(object)RpcVoid.Instance);
                }

                var value = _serializer.Deserialize<TArg>(payload);
                return handler(value);
            }));

            if (!registered)
                throw new InvalidOperationException(
                    $"Notification handler already registered for {method.ServiceId}:{method.MethodId}.");
        }

        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public void RegisterRawNotificationHandler(
            int serviceId,
            int methodId,
            Func<ReadOnlyMemory<byte>, ValueTask> handler)
        {
            ThrowIfDisposed();
            if (handler is null) throw new ArgumentNullException(nameof(handler));

            var registered = _notificationHandlers.TryAdd((serviceId, methodId), new RegisteredNotificationHandler(
                typeof(ReadOnlyMemory<byte>),
                payload => handler(payload)));

            if (!registered)
                throw new InvalidOperationException(
                    $"Notification handler already registered for {serviceId}:{methodId}.");
        }

        /// <summary>
        ///     Registers a synchronous handler for a server-to-client notification method.
        /// </summary>
        public void RegisterNotificationHandler<TArg>(RpcNotificationMethod<TArg> method, Action<TArg> handler)
        {
            if (handler is null) throw new ArgumentNullException(nameof(handler));
            RegisterNotificationHandler(method, arg =>
            {
                handler(arg);
                return default;
            });
        }

        /// <inheritdoc />
        public async ValueTask<TResult> CallAsync<TArg, TResult>(RpcMethod<TArg, TResult> method, TArg? arg,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();
            var reservation = _pending.Reserve(ref _nextId);
            var id = reservation.RequestId;
            var tcs = reservation.CompletionSource;
            var startedAt = Stopwatch.GetTimestamp();

            try
            {
                using var requestWriter = RpcEnvelopeCodec.BeginRequestPayload(
                    id,
                    method.ServiceId,
                    method.MethodId);
                if (arg is not null)
                {
                    _serializer.Serialize(requestWriter, arg);
                }

                _requestLogger.LogTrace(
                    "RPC request sent {RequestId} service {ServiceId} method {MethodId}.",
                    id,
                    method.ServiceId,
                    method.MethodId);

                using var reqBytes = RpcEnvelopeCodec.CompletePayload(requestWriter);
                await SendFrameAsyncSerialized(reqBytes.Memory, ct).ConfigureAwait(false);

                using var reg = ct.Register(() =>
                {
                    _pending.TryCancel(id, ct);
                });

                using var resp = await tcs.Task.ConfigureAwait(false);
                LogRequestCompleted(
                    id,
                    method.ServiceId,
                    method.MethodId,
                    resp.Status,
                    GetElapsedTime(startedAt),
                    resp.ErrorMessage);
                if (resp.Status != RpcStatus.Ok)
                    throw new RpcException(resp.Status, resp.ErrorMessage, id, method.ServiceId, method.MethodId);

                if (typeof(TResult) == typeof(RpcVoid))
                    return (TResult)(object)RpcVoid.Instance;

                return _serializer.Deserialize<TResult>(resp.Payload.Memory)!;
            }
            finally
            {
                _pending.Remove(id);
            }
        }

        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public async ValueTask<TransportFrame> CallRawAsync(
            int serviceId,
            int methodId,
            ReadOnlyMemory<byte> payload,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();
            var reservation = _pending.Reserve(ref _nextId);
            var id = reservation.RequestId;
            var tcs = reservation.CompletionSource;

            try
            {
                var req = new RpcRequestEnvelope
                {
                    RequestId = id,
                    ServiceId = serviceId,
                    MethodId = methodId,
                    Payload = payload
                };

                var reqBytes = RpcEnvelopeCodec.EncodeRequest(req);
                return await CompleteRawCallAsync(
                        id,
                        serviceId,
                        methodId,
                        tcs,
                        reqBytes,
                        ct)
                    .ConfigureAwait(false);
            }
            finally
            {
                _pending.Remove(id);
            }
        }

        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public async ValueTask<TransportFrame> CallRawAsync(
            int serviceId,
            int methodId,
            Action<IBufferWriter<byte>> writePayload,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (writePayload is null) throw new ArgumentNullException(nameof(writePayload));
            var reservation = _pending.Reserve(ref _nextId);
            var id = reservation.RequestId;
            var tcs = reservation.CompletionSource;

            try
            {
                var reqBytes = RpcEnvelopeCodec.EncodeRequest(
                    id,
                    serviceId,
                    methodId,
                    writePayload);
                return await CompleteRawCallAsync(
                        id,
                        serviceId,
                        methodId,
                        tcs,
                        reqBytes,
                        ct)
                    .ConfigureAwait(false);
            }
            finally
            {
                _pending.Remove(id);
            }
        }

        private async ValueTask<TransportFrame> CompleteRawCallAsync(
            uint id,
            int serviceId,
            int methodId,
            TaskCompletionSource<RpcResponseFrame> completion,
            TransportFrame request,
            CancellationToken cancellationToken)
        {
            var startedAt = Stopwatch.GetTimestamp();
            using (request)
            {
                _requestLogger.LogTrace(
                    "RPC request sent {RequestId} service {ServiceId} method {MethodId}.",
                    id,
                    serviceId,
                    methodId);
                await SendFrameAsyncSerialized(request.Memory, cancellationToken)
                    .ConfigureAwait(false);
            }
            using var registration = cancellationToken.Register(() =>
            {
                _pending.TryCancel(id, cancellationToken);
            });

            using var response = await completion.Task.ConfigureAwait(false);
            LogRequestCompleted(
                id,
                serviceId,
                methodId,
                response.Status,
                GetElapsedTime(startedAt),
                response.ErrorMessage);
            if (response.Status != RpcStatus.Ok)
            {
                throw new RpcException(
                    response.Status,
                    response.ErrorMessage,
                    id,
                    serviceId,
                    methodId);
            }

            return response.Payload.Slice(0, response.Payload.Length);
        }

        /// <summary>
        ///     Stops background loops, fails pending requests, and disposes the transport.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            try { _cts.Cancel(); } catch (ObjectDisposedException) { }
            _pending.FailAll(new ObjectDisposedException(nameof(RpcClientRuntime)));
            Interlocked.Exchange(ref _started, 0);
            if (_recvLoop is not null)
                try
                {
                    await _recvLoop.ConfigureAwait(false);
                }
                catch
                {
                }

            if (_keepAliveLoop is not null)
                try
                {
                    await _keepAliveLoop.ConfigureAwait(false);
                }
                catch
                {
                }

            _pushQueue.Writer.TryComplete();
            if (_pushLoop is not null)
                try
                {
                    await _pushLoop.ConfigureAwait(false);
                }
                catch
                {
                }

            await _transport.DisposeAsync().ConfigureAwait(false);
            _connection.Dispose();
            try { _cts.Dispose(); } catch (ObjectDisposedException) { }
        }

        private async Task ReceiveLoopAsync()
        {
            var ct = _cts.Token;
            Exception? err = null;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    using var frame = await _connection.ReceiveApplicationFrameAsync(ct).ConfigureAwait(false);
                    if (frame.IsEmpty)
                        throw new InvalidOperationException("Transport closed.");

                    var frameType = RpcEnvelopeCodec.PeekFrameType(frame.Span);
                    switch (frameType)
                    {
                        case RpcFrameType.Response:
                        {
                            var resp = RpcEnvelopeCodec.DecodeResponse(frame);
                            _pending.Complete(resp);
                            break;
                        }
                        case RpcFrameType.Push:
                        {
                            var push = RpcEnvelopeCodec.DecodePush(frame);
                            var shouldWarn = TrackNotificationEnqueued(
                                push,
                                out var queuedCount,
                                out var queuedBytes);
                            if (!_pushQueue.Writer.TryWrite(push))
                            {
                                TrackNotificationDequeued(push);
                                push.Dispose();
                            }
                            else if (shouldWarn)
                            {
                                LogNotificationBacklog(
                                    push,
                                    queuedCount,
                                    queuedBytes);
                            }

                            break;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                    err = ex;
            }
            finally
            {
                if (err is null)
                    err = _disconnectReason;
                if (err is not null)
                    _pending.FailAll(err);

                _pushQueue.Writer.TryComplete();
                Disconnected?.Invoke(err);
            }
        }

        private async Task ProcessPushLoopAsync()
        {
            try
            {
                await foreach (var push in _pushQueue.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false))
                {
                    TrackNotificationDequeued(push);
                    using (push)
                    {
                        if (!_notificationHandlers.TryGetValue((push.ServiceId, push.MethodId), out var registration))
                        {
                            _requestLogger.LogWarning(
                                "RPC notification unhandled service {ServiceId} method {MethodId} payloadBytes {PayloadBytes}.",
                                push.ServiceId,
                                push.MethodId,
                                push.Payload.Length);
                            NotifyDiagnosticObservers(
                                UnhandledNotificationReceived,
                                new RpcUnhandledNotificationContext(
                                    push.ServiceId,
                                    push.MethodId,
                                    push.Payload.Length),
                                nameof(UnhandledNotificationReceived),
                                push.ServiceId,
                                push.MethodId);
                            continue;
                        }

                        _requestLogger.LogTrace(
                            "RPC notification received service {ServiceId} method {MethodId} payloadBytes {PayloadBytes}.",
                            push.ServiceId,
                            push.MethodId,
                            push.Payload.Length);

                        try
                        {
                            ValueTask DispatchAsync()
                            {
                                return registration.Handler(push.Payload.Memory);
                            }

                            var middleware = _notificationDispatchMiddleware;
                            if (middleware is null)
                            {
                                await DispatchAsync().ConfigureAwait(false);
                            }
                            else
                            {
                                await middleware(push.Metadata, DispatchAsync).ConfigureAwait(false);
                            }
                        }
                        catch (Exception ex)
                        {
                            _requestLogger.LogError(
                                ex,
                                "RPC notification handler failed service {ServiceId} method {MethodId}.",
                                push.ServiceId,
                                push.MethodId);
                            NotifyDiagnosticObservers(
                                NotificationHandlerException,
                                new RpcNotificationHandlerExceptionContext(
                                    push.ServiceId,
                                    push.MethodId,
                                    registration.PayloadType,
                                    ex),
                                nameof(NotificationHandlerException),
                                push.ServiceId,
                                push.MethodId);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ChannelClosedException)
            {
            }
            finally
            {
                while (_pushQueue.Reader.TryRead(out var push))
                {
                    TrackNotificationDequeued(push);
                    push.Dispose();
                }
            }
        }

        private async Task KeepAliveLoopAsync()
        {
            await _connection.RunKeepAliveAsync(
                "RPC keepalive timed out.",
                ex =>
                {
                    SetDisconnectReason(ex);
                    try
                    {
                        _cts.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                },
                _cts.Token).ConfigureAwait(false);
        }

        private ValueTask SendFrameAsyncSerialized(ReadOnlyMemory<byte> frame, CancellationToken ct)
        {
            return _connection.SendAsync(frame, ct);
        }

        private void NotifyDiagnosticObservers<TContext>(
            Action<TContext>? observers,
            TContext context,
            string eventName,
            int serviceId,
            int methodId)
        {
            if (observers is null)
                return;

            foreach (Action<TContext> observer in observers.GetInvocationList())
            {
                try
                {
                    observer(context);
                }
                catch (Exception ex)
                {
                    _requestLogger.LogError(
                        ex,
                        "RPC notification diagnostic subscriber failed for {DiagnosticEvent} service {ServiceId} method {MethodId}.",
                        eventName,
                        serviceId,
                        methodId);
                }
            }
        }

        private bool TrackNotificationEnqueued(
            RpcPushFrame push,
            out long queuedCount,
            out long queuedBytes)
        {
            queuedCount = Interlocked.Increment(ref _queuedNotificationCount);
            queuedBytes = Interlocked.Add(
                ref _queuedNotificationBytes,
                push.EncodedLength);
            var countThreshold = _nextNotificationQueueCountWarning;
            var bytesThreshold = _nextNotificationQueueBytesWarning;
            var crossedCount = queuedCount >= countThreshold;
            var crossedBytes = queuedBytes >= bytesThreshold;
            if (!crossedCount && !crossedBytes)
            {
                return false;
            }

            if (crossedCount)
            {
                _nextNotificationQueueCountWarning =
                    NextWarningThreshold(countThreshold);
            }

            if (crossedBytes)
            {
                _nextNotificationQueueBytesWarning =
                    NextWarningThreshold(bytesThreshold);
            }

            return true;
        }

        private void LogNotificationBacklog(
            RpcPushFrame push,
            long queuedCount,
            long queuedBytes)
        {
            _requestLogger.LogWarning(
                "RPC notification backlog reached a new high-water threshold: {QueuedNotifications} queued notifications and {QueuedBytes} queued wire bytes after service {ServiceId} method {MethodId}. The receive queue remains unbounded and no notification was dropped.",
                queuedCount,
                queuedBytes,
                push.ServiceId,
                push.MethodId);
        }

        private void TrackNotificationDequeued(RpcPushFrame push)
        {
            Interlocked.Decrement(ref _queuedNotificationCount);
            Interlocked.Add(ref _queuedNotificationBytes, -push.EncodedLength);
        }

        private static long NextWarningThreshold(long current)
        {
            return current > long.MaxValue / 2
                ? long.MaxValue
                : current * 2;
        }

        private static TimeSpan GetElapsedTime(long startedAt)
        {
            var elapsedTicks = Stopwatch.GetTimestamp() - startedAt;
            return TimeSpan.FromSeconds(
                elapsedTicks / (double)Stopwatch.Frequency);
        }

        private void SetDisconnectReason(Exception ex)
        {
            if (Interlocked.CompareExchange(ref _disconnectReasonSet, 1, 0) == 0)
                _disconnectReason = ex;
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0)
                throw new ObjectDisposedException(nameof(RpcClientRuntime));
        }

        private void LogRequestCompleted(
            uint requestId,
            int serviceId,
            int methodId,
            RpcStatus status,
            TimeSpan elapsed,
            string? errorMessage)
        {
            if (status == RpcStatus.Ok)
            {
                _requestLogger.LogTrace(
                    "RPC request completed {RequestId} status {Status} service {ServiceId} method {MethodId} in {ElapsedMs}ms.",
                    requestId,
                    status,
                    serviceId,
                    methodId,
                    elapsed.TotalMilliseconds);
                return;
            }

            _requestLogger.LogWarning(
                "RPC request completed {RequestId} status {Status} service {ServiceId} method {MethodId} in {ElapsedMs}ms. {ErrorMessage}",
                requestId,
                status,
                serviceId,
                methodId,
                elapsed.TotalMilliseconds,
                errorMessage);
        }

        private sealed class RegisteredNotificationHandler
        {
            public RegisteredNotificationHandler(Type payloadType, RpcNotificationPayloadHandler handler)
            {
                PayloadType = payloadType;
                Handler = handler;
            }

            public Type PayloadType { get; }

            public RpcNotificationPayloadHandler Handler { get; }
        }
    }
}
