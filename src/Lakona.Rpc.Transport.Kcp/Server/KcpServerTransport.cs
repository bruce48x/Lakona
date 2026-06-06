using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.Sockets.Kcp;
using System.Runtime.InteropServices;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Transport.Kcp
{
    /// <summary>
    ///     ITransport implementation over KCP (UDP).
    ///     Uses the same length-prefix framing (4-byte big-endian + payload) as other transports.
    /// </summary>
    public sealed class KcpServerTransport : ITransport, IKcpCallback, IRentable, IRemoteEndPointProvider
    {
        private const int MaxFrameSize = RpcProtocolLimits.DefaultMaxTransportFrameSize;
        private readonly ConcurrentQueue<TransportFrame> _frames = new();
        private readonly SemaphoreSlim _frameSignal = new(0);
        private readonly SimpleSegManager.Kcp _kcp;
        private readonly object _kcpGate = new();
        private readonly Action? _onDispose;
        private readonly EndPoint _remote;
        private readonly Socket _socket;
        private readonly LengthPrefixedFrameAccumulator _accumulator = new();
        private readonly CancellationTokenSource _cts = new();
        private IDisposable? _updateRegistration;
        private int _isConnected;
        private int _disposed;

        public KcpServerTransport(Socket socket, EndPoint remote, uint conv, Action? onDispose = null)
        {
            _socket = socket ?? throw new ArgumentNullException(nameof(socket));
            _remote = remote ?? throw new ArgumentNullException(nameof(remote));
            _onDispose = onDispose;

            _kcp = new SimpleSegManager.Kcp(conv, this, this);
        }

        void IKcpCallback.Output(IMemoryOwner<byte> buffer, int avalidLength)
        {
            try
            {
                var mem = buffer.Memory.Slice(0, avalidLength);
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

        public EndPoint? RemoteEndPoint => _remote;

        public bool IsConnected => Volatile.Read(ref _isConnected) != 0;

        public ValueTask ConnectAsync(CancellationToken ct = default)
        {
            if (IsConnected)
                return default;

            Volatile.Write(ref _isConnected, 1);
            _updateRegistration = KcpUpdateScheduler.Register(UpdateKcp);

            return default;
        }

        public ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
        {
            if (!IsConnected)
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
            if (!IsConnected)
                throw new InvalidOperationException("Not connected.");

            while (true)
            {
                if (TryDequeueFrame(out var queued))
                    return queued;

                await _frameSignal.WaitAsync(ct).ConfigureAwait(false);
                if (!IsConnected && _frames.IsEmpty)
                    return TransportFrame.Empty;
            }
        }

        public async ValueTask DisposeAsync()
        {
            Volatile.Write(ref _isConnected, 0);

            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            _updateRegistration?.Dispose();
            _updateRegistration = null;
            _kcp.Dispose();
            try
            {
                _frameSignal.Release();
            }
            catch (SemaphoreFullException)
            {
            }

            try
            {
                _cts.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }

            _onDispose?.Invoke();
            while (_frames.TryDequeue(out var frame))
                frame.Dispose();
        }

        internal void ProcessDatagram(ReadOnlySpan<byte> data)
        {
            if (!IsConnected)
                return;

            lock (_kcpGate)
            {
                _kcp.Input(data);
                DrainKcp();
            }
        }

        private void DrainKcp()
        {
            while (true)
            {
                var size = _kcp.PeekSize();
                if (size <= 0)
                    break;

                if (size > MaxFrameSize)
                    throw new InvalidOperationException($"Frame too large: {size} bytes");

                using var payload = TransportFrame.Allocate(size);
                _kcp.Recv(payload.GetWritableSpan());
                AppendAndUnpack(payload.Span);
            }
        }

        private void AppendAndUnpack(ReadOnlySpan<byte> payload)
        {
            _accumulator.Append(payload, MaxFrameSize);

            while (_accumulator.TryReadFrame(out var frame))
                EnqueueFrame(frame);
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

        private void EnqueueFrame(TransportFrame frame)
        {
            _frames.Enqueue(frame);
            _frameSignal.Release();
        }

        private void UpdateKcp()
        {
            lock (_kcpGate)
            {
                var now = DateTimeOffset.UtcNow;
                _kcp.Update(in now);
            }
        }
    }
}
