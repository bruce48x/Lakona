using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.Sockets.Kcp;
using System.Runtime.InteropServices;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Transport.Kcp
{
    public sealed class KcpTransport : ITransport, IKcpCallback, IRentable
    {
        private const int MaxFrameSize = RpcProtocolLimits.DefaultMaxTransportFrameSize;
        private const int ReceiveBufferSize = 64 * 1024;
        private readonly ConcurrentQueue<TransportFrame> _frames = new();
        private readonly object _kcpGate = new();
        private readonly string _host;
        private readonly int _port;
        private readonly uint? _conversationId;
        private readonly LengthPrefixedFrameAccumulator _accumulator = new();
        private readonly EndPoint _receiveAny = new IPEndPoint(IPAddress.Any, 0);
        private IDisposable? _updateRegistration;
        private SimpleSegManager.Kcp? _kcp;
        private EndPoint? _remote;
        private Socket? _socket;
        private byte[]? _receiveBuffer;

        public KcpTransport(string host, int port)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _port = port;
        }

        public KcpTransport(string host, int port, uint conversationId)
            : this(host, port)
        {
            if (conversationId == 0)
                throw new ArgumentOutOfRangeException(nameof(conversationId), "Conversation id must be non-zero.");

            _conversationId = conversationId;
        }

        public bool IsConnected { get; private set; }

        public async ValueTask ConnectAsync(CancellationToken ct = default)
        {
            if (IsConnected)
                return;

            var ipAddress = await ResolveHostAsync(_host).ConfigureAwait(false);
            var bootstrapEndPoint = new IPEndPoint(ipAddress, _port);
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            try
            {
                socket.Bind(new IPEndPoint(IPAddress.Any, 0));

                var conv = _conversationId ?? CreateConversationId();
                socket.SendTo(CreateHandshakeRequest(conv), bootstrapEndPoint);

                var sessionPort = await ReceiveHandshakeAckAsync(socket, bootstrapEndPoint, conv, ct).ConfigureAwait(false);
                _remote = new IPEndPoint(ipAddress, sessionPort);
                _socket = socket;
                _kcp = new SimpleSegManager.Kcp(conv, this, this);
                _updateRegistration = KcpUpdateScheduler.Register(UpdateKcp);
                IsConnected = true;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        public ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
        {
            if (!IsConnected || _kcp is null)
                throw new InvalidOperationException("Not connected.");

            using var packed = LengthPrefix.Pack(frame.Span);
            lock (_kcpGate)
            {
                _kcp.Send(packed.Span, null!);
                var now = DateTimeOffset.UtcNow;
                _kcp.Update(in now);
            }

            return default;
        }

        public async ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken ct = default)
        {
            if (!IsConnected || _socket is null || _remote is null)
                throw new InvalidOperationException("Not connected.");

            var buffer = _receiveBuffer ??= ArrayPool<byte>.Shared.Rent(ReceiveBufferSize);
            while (true)
            {
                if (TryDequeueFrame(out var queued))
                    return queued;

#if NET8_0_OR_GREATER
                var received = await _socket.ReceiveFromAsync(buffer, SocketFlags.None, _receiveAny, ct).ConfigureAwait(false);
#else
                var received = await ReceiveFromAsync(_socket, buffer, ct).ConfigureAwait(false);
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
            IsConnected = false;
            _updateRegistration?.Dispose();
            _updateRegistration = null;

            _kcp?.Dispose();

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

        private async Task<int> ReceiveHandshakeAckAsync(
            Socket socket,
            EndPoint bootstrapEndPoint,
            uint conv,
            CancellationToken ct)
        {
            var buffer = new byte[32];
            EndPoint any = new IPEndPoint(IPAddress.Any, 0);

            while (true)
            {
#if NET8_0_OR_GREATER
                SocketReceiveFromResult received;
                try
                {
                    received = await socket.ReceiveFromAsync(buffer, SocketFlags.None, any, ct).ConfigureAwait(false);
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.MessageSize)
                {
                    continue;
                }
#else
                SocketReceiveFromResult received;
                try
                {
                    received = await ReceiveFromAsync(socket, buffer, ct).ConfigureAwait(false);
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.MessageSize)
                {
                    continue;
                }
#endif
                if (!EndPointEquals(received.RemoteEndPoint, bootstrapEndPoint))
                    continue;

                var packet = buffer.AsSpan(0, received.ReceivedBytes);
                if (packet.Length != 12)
                    continue;
                if (!packet.Slice(0, 4).SequenceEqual("UACK"u8))
                    continue;
                if (BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(4, 4)) != conv)
                    continue;

                return BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(8, 4));
            }
        }

        private void ProcessInput(ReadOnlySpan<byte> data)
        {
            lock (_kcpGate)
            {
                _kcp!.Input(data);
                DrainKcp();
            }
        }

        private void DrainKcp()
        {
            while (true)
            {
                var size = _kcp!.PeekSize();
                if (size <= 0)
                    break;

                if (size > MaxFrameSize)
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
            _accumulator.Append(payload, MaxFrameSize);

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

        private void UpdateKcp()
        {
            lock (_kcpGate)
            {
                if (_kcp is not null)
                {
                    var now = DateTimeOffset.UtcNow;
                    _kcp.Update(in now);
                }
            }
        }

        private static byte[] CreateHandshakeRequest(uint conv)
        {
            var buffer = new byte[8];
            "UKCP"u8.CopyTo(buffer);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4, 4), conv);
            return buffer;
        }

        private static uint CreateConversationId()
        {
            var guid = Guid.NewGuid().ToByteArray();
            var conv = BinaryPrimitives.ReadUInt32LittleEndian(guid);
            return conv == 0 ? 1u : conv;
        }

        private static async Task<IPAddress> ResolveHostAsync(string host)
        {
            if (IPAddress.TryParse(host, out var address))
                return address;

            var addresses = await Dns.GetHostAddressesAsync(host).ConfigureAwait(false);
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
