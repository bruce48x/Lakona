using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.Sockets.Kcp;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Transport.Kcp
{
    public sealed class KcpTransport : ITransport, IKcpCallback, IRentable
    {
        private const int MaxBufferedBytes = RpcProtocolLimits.DefaultMaxLengthPrefixedFrameSize;
        private const int ReceiveBufferSize = 64 * 1024;
        private static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan DefaultHandshakeRetryInterval = TimeSpan.FromMilliseconds(250);
        private readonly ConcurrentQueue<TransportFrame> _frames = new();
        private readonly object _kcpGate = new();
        private readonly string _host;
        private readonly int _port;
        private readonly uint? _conversationId;
        private readonly TimeSpan _connectTimeout;
        private readonly TimeSpan _handshakeRetryInterval;
        private readonly LengthPrefixedFrameAccumulator _accumulator = new();
        private readonly EndPoint _receiveAny = new IPEndPoint(IPAddress.Any, 0);
        private IKcpUpdateRegistration? _updateRegistration;
        private ExceptionDispatchInfo? _terminalFailure;
        private SimpleSegManager.Kcp? _kcp;
        private EndPoint? _remote;
        private Socket? _socket;
        private byte[]? _receiveBuffer;
        private int _isConnected;
        private int _disposed;

        public KcpTransport(string host, int port)
            : this(host, port, null, DefaultConnectTimeout, DefaultHandshakeRetryInterval)
        {
        }

        public KcpTransport(string host, int port, uint conversationId)
            : this(host, port, conversationId, DefaultConnectTimeout, DefaultHandshakeRetryInterval)
        {
        }

        internal KcpTransport(
            string host,
            int port,
            uint? conversationId,
            TimeSpan connectTimeout,
            TimeSpan handshakeRetryInterval)
        {
            if (conversationId == 0)
                throw new ArgumentOutOfRangeException(nameof(conversationId), "Conversation id must be non-zero.");
            if (connectTimeout <= TimeSpan.Zero || connectTimeout == Timeout.InfiniteTimeSpan)
                throw new ArgumentOutOfRangeException(nameof(connectTimeout), "Connect timeout must be finite and positive.");
            if (handshakeRetryInterval <= TimeSpan.Zero || handshakeRetryInterval >= connectTimeout)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(handshakeRetryInterval),
                    "Handshake retry interval must be positive and shorter than the connect timeout.");
            }

            _host = host ?? throw new ArgumentNullException(nameof(host));
            _port = port;
            _conversationId = conversationId;
            _connectTimeout = connectTimeout;
            _handshakeRetryInterval = handshakeRetryInterval;
        }

        public bool IsConnected => Volatile.Read(ref _isConnected) != 0;

        public async ValueTask ConnectAsync(CancellationToken ct = default)
        {
            if (IsConnected)
                return;

            ct.ThrowIfCancellationRequested();
            using var deadline = new CancellationTokenSource(_connectTimeout);
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
            try
            {
                var ipAddress = await ResolveHostAsync(_host, attempt.Token).ConfigureAwait(false);
                var bootstrapEndPoint = new IPEndPoint(ipAddress, _port);
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                try
                {
                    socket.Bind(new IPEndPoint(IPAddress.Any, 0));

                    var conv = _conversationId ?? CreateConversationId();
                    var sessionPort = await ReceiveHandshakeResponseAsync(
                            socket,
                            bootstrapEndPoint,
                            conv,
                            attempt.Token)
                        .ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();
                    deadline.Token.ThrowIfCancellationRequested();
                    _remote = new IPEndPoint(ipAddress, sessionPort);
                    _socket = socket;
                    _kcp = new SimpleSegManager.Kcp(conv, this, this);
                    _updateRegistration = KcpUpdateScheduler.Register(UpdateKcp, Fail);
                    Volatile.Write(ref _isConnected, 1);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                ct.ThrowIfCancellationRequested();
                throw;
            }
            catch (OperationCanceledException exception) when (deadline.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"The KCP connection attempt did not complete within {_connectTimeout}.",
                    exception);
            }
        }

        public ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
        {
            if (!IsConnected || _kcp is null)
            {
                ThrowIfFailed();
                throw new InvalidOperationException("Not connected.");
            }

            using var packed = LengthPrefix.Pack(frame.Span);
            DateTimeOffset nextUpdate;
            lock (_kcpGate)
            {
                _kcp.Send(packed.Span, null!);
                var now = DateTimeOffset.UtcNow;
                _kcp.Update(in now);
                nextUpdate = _kcp.Check(in now);
            }

            _updateRegistration?.Reschedule(nextUpdate);

            return default;
        }

        public async ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken ct = default)
        {
            if (!IsConnected || _socket is null || _remote is null)
            {
                ThrowIfFailed();
                throw new InvalidOperationException("Not connected.");
            }

            var buffer = _receiveBuffer ??= ArrayPool<byte>.Shared.Rent(ReceiveBufferSize);
            while (true)
            {
                if (TryDequeueFrame(out var queued))
                    return queued;

#if NET8_0_OR_GREATER
                SocketReceiveFromResult received;
                try
                {
                    received = await _socket.ReceiveFromAsync(buffer, SocketFlags.None, _receiveAny, ct).ConfigureAwait(false);
                }
                catch when (Volatile.Read(ref _terminalFailure) is not null)
                {
                    ThrowIfFailed();
                    throw;
                }
#else
                SocketReceiveFromResult received;
                try
                {
                    received = await ReceiveFromAsync(_socket, buffer, ct).ConfigureAwait(false);
                }
                catch when (Volatile.Read(ref _terminalFailure) is not null)
                {
                    ThrowIfFailed();
                    throw;
                }
#endif
                if (!EndPointEquals(received.RemoteEndPoint, _remote))
                    continue;

                ProcessInput(buffer.AsSpan(0, received.ReceivedBytes));

                if (TryDequeueFrame(out var frame))
                    return frame;
            }
        }

        public async ValueTask DisposeAsync()
        {
            Volatile.Write(ref _isConnected, 0);
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _updateRegistration?.Dispose();
            _updateRegistration = null;

            lock (_kcpGate)
            {
                _kcp?.Dispose();
                _kcp = null;
            }

            var receiveBuffer = Interlocked.Exchange(ref _receiveBuffer, null);
            if (receiveBuffer is not null)
                ArrayPool<byte>.Shared.Return(receiveBuffer);

            while (_frames.TryDequeue(out var frame))
                frame.Dispose();

            try
            {
                _socket?.Dispose();
            }
            catch
            {
            }
        }

        void IKcpCallback.Output(IMemoryOwner<byte> buffer, int avalidLength)
        {
            try
            {
                if (_socket is null || _remote is null)
                    return;

                var mem = buffer.Memory.Slice(0, avalidLength);
#if NET8_0_OR_GREATER
                _socket.SendTo(mem.Span, SocketFlags.None, _remote);
#else
                if (MemoryMarshal.TryGetArray(mem, out ArraySegment<byte> segment))
                {
                    _socket.SendTo(segment.Array!, segment.Offset, segment.Count, SocketFlags.None, _remote);
                }
                else
                {
                    var tmp = ArrayPool<byte>.Shared.Rent(mem.Length);
                    try
                    {
                        mem.Span.CopyTo(tmp);
                        _socket.SendTo(tmp, 0, mem.Length, SocketFlags.None, _remote);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(tmp);
                    }
                }
#endif
            }
            finally
            {
                buffer.Dispose();
            }
        }

        IMemoryOwner<byte> IRentable.RentBuffer(int size)
        {
            return MemoryPool<byte>.Shared.Rent(size);
        }

        private async Task<int> ReceiveHandshakeResponseAsync(
            Socket socket,
            EndPoint bootstrapEndPoint,
            uint conv,
            CancellationToken ct)
        {
            var buffer = new byte[32];
            EndPoint any = new IPEndPoint(IPAddress.Any, 0);
            var request = KcpHandshake.CreateRequest(conv);
            Task<SocketReceiveFromResult>? receiveTask = null;
            using var receiveCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
            try
            {
                while (true)
                {
                    socket.SendTo(request, bootstrapEndPoint);
                    var retry = Task.Delay(_handshakeRetryInterval, ct);
                    while (true)
                    {
                        receiveTask ??= ReceiveHandshakePacketAsync(
                            socket,
                            buffer,
                            any,
                            receiveCancellation.Token);
                        if (await Task.WhenAny(receiveTask, retry).ConfigureAwait(false) != receiveTask)
                        {
                            await retry.ConfigureAwait(false);
                            break;
                        }

                        SocketReceiveFromResult received;
                        try
                        {
                            received = await receiveTask.ConfigureAwait(false);
                        }
                        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.MessageSize)
                        {
                            receiveTask = null;
                            continue;
                        }

                        receiveTask = null;
                        if (!EndPointEquals(received.RemoteEndPoint, bootstrapEndPoint))
                            continue;

                        var packet = buffer.AsSpan(0, received.ReceivedBytes);
                        if (KcpHandshake.TryParseAck(packet, conv, out var sessionPort))
                            return sessionPort;

                        if (KcpHandshake.TryParseReject(packet, conv, out var reason))
                            throw new KcpConnectionRejectedException(reason);
                    }
                }
            }
            finally
            {
                receiveCancellation.Cancel();
                if (receiveTask is not null)
                {
                    try
                    {
                        await receiveTask.ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static Task<SocketReceiveFromResult> ReceiveHandshakePacketAsync(
            Socket socket,
            byte[] buffer,
            EndPoint any,
            CancellationToken ct)
        {
#if NET8_0_OR_GREATER
            return socket.ReceiveFromAsync(buffer, SocketFlags.None, any, ct).AsTask();
#else
            return ReceiveFromAsync(socket, buffer, ct);
#endif
        }

        private void ProcessInput(ReadOnlySpan<byte> data)
        {
            lock (_kcpGate)
            {
                _kcp!.Input(data);
                DrainKcp();
            }

            _updateRegistration?.Reschedule(DateTimeOffset.UtcNow);
        }

        private void DrainKcp()
        {
            while (true)
            {
                var size = _kcp!.PeekSize();
                if (size <= 0)
                    break;

                if (size > MaxBufferedBytes)
                    throw new InvalidOperationException($"Frame too large: {size} bytes");

                var payload = ArrayPool<byte>.Shared.Rent(size);
                try
                {
                    _kcp.Recv(payload.AsSpan(0, size));
                    AppendAndUnpack(payload.AsSpan(0, size));
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(payload);
                }
            }
        }

        private void AppendAndUnpack(ReadOnlySpan<byte> payload)
        {
            _accumulator.Append(payload);

            while (_accumulator.TryReadFrame(out var frame))
                _frames.Enqueue(frame);
        }

        private bool TryDequeueFrame(out TransportFrame frame)
        {
            if (_frames.TryDequeue(out var queued))
            {
                frame = queued;
                return true;
            }

            frame = TransportFrame.Empty;
            return false;
        }

        private DateTimeOffset UpdateKcp(DateTimeOffset now)
        {
            lock (_kcpGate)
            {
                if (IsConnected && _kcp is not null)
                {
                    _kcp.Update(in now);
                    return _kcp.Check(in now);
                }

                return DateTimeOffset.MaxValue;
            }
        }

        private void Fail(Exception exception)
        {
            var failure = ExceptionDispatchInfo.Capture(exception);
            if (Interlocked.CompareExchange(ref _terminalFailure, failure, null) is not null)
                return;

            Volatile.Write(ref _isConnected, 0);
            try
            {
                _socket?.Dispose();
            }
            catch
            {
            }
        }

        private void ThrowIfFailed()
        {
            Volatile.Read(ref _terminalFailure)?.Throw();
        }

        private static uint CreateConversationId()
        {
            var guid = Guid.NewGuid().ToByteArray();
            var conv = BinaryPrimitives.ReadUInt32LittleEndian(guid);
            return conv == 0 ? 1u : conv;
        }

        private static async Task<IPAddress> ResolveHostAsync(string host, CancellationToken ct)
        {
            if (IPAddress.TryParse(host, out var address))
                return address;

            var resolution = Dns.GetHostAddressesAsync(host);
            var cancellation = Task.Delay(Timeout.InfiniteTimeSpan, ct);
            if (await Task.WhenAny(resolution, cancellation).ConfigureAwait(false) != resolution)
            {
                _ = resolution.ContinueWith(
                    static task => _ = task.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                ct.ThrowIfCancellationRequested();
            }

            var addresses = await resolution.ConfigureAwait(false);
            foreach (var candidate in addresses)
            {
                if (candidate.AddressFamily == AddressFamily.InterNetwork)
                    return candidate;
            }

            throw new InvalidOperationException($"Unable to resolve IPv4 endpoint for '{host}'.");
        }

        private static bool EndPointEquals(EndPoint? left, EndPoint? right)
        {
            return left is not null && right is not null && left.Equals(right);
        }

#if !NET8_0_OR_GREATER
        private static async Task<SocketReceiveFromResult> ReceiveFromAsync(Socket socket, byte[] buffer, CancellationToken ct)
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                if (!socket.Poll(10_000, SelectMode.SelectRead))
                {
                    await Task.Delay(10, ct).ConfigureAwait(false);
                    continue;
                }

                EndPoint receiveFrom = new IPEndPoint(IPAddress.Any, 0);
                try
                {
                    var receivedBytes = socket.ReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref receiveFrom);
                    return new SocketReceiveFromResult
                    {
                        ReceivedBytes = receivedBytes,
                        RemoteEndPoint = receiveFrom
                    };
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.MessageSize)
                {
                    throw;
                }
                catch (ObjectDisposedException) when (ct.IsCancellationRequested)
                {
                    throw new OperationCanceledException(ct);
                }
                catch (SocketException) when (ct.IsCancellationRequested)
                {
                    throw new OperationCanceledException(ct);
                }
            }
        }
#endif
    }
}
