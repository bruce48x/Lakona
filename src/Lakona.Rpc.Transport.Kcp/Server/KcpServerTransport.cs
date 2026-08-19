using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Net.Sockets.Kcp;
using System.Runtime.InteropServices;
using System.Runtime.ExceptionServices;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Transport.Kcp
{
    /// <summary>
    ///     ITransport implementation over KCP (UDP).
    ///     Uses the same length-prefix framing (4-byte big-endian + payload) as other transports.
    /// </summary>
    public sealed class KcpServerTransport : ITransport, IKcpCallback, IRentable, IRemoteEndPointProvider
    {
        private const int MaxBufferedBytes = RpcProtocolLimits.DefaultMaxLengthPrefixedFrameSize;
        private readonly SemaphoreSlim _receiveSignal = new(0, 1);
        private readonly SimpleSegManager.Kcp _kcp;
        private readonly object _kcpGate = new();
        private readonly Action? _onDispose;
        private readonly EndPoint _remote;
        private readonly Socket _socket;
        private readonly LengthPrefixedFrameAccumulator _accumulator = new();
        private readonly CancellationTokenSource _cts = new();
        private IKcpUpdateRegistration? _updateRegistration;
        private ExceptionDispatchInfo? _terminalFailure;
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
            _updateRegistration = KcpUpdateScheduler.Register(UpdateKcp, Fail);

            return default;
        }

        public ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
        {
            if (!IsConnected)
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
            if (!IsConnected)
            {
                ThrowIfFailed();
                throw new InvalidOperationException("Not connected.");
            }

            while (true)
            {
                if (TryReadFrame(out var frame))
                    return frame;

                await _receiveSignal.WaitAsync(ct).ConfigureAwait(false);
                if (!IsConnected)
                {
                    ThrowIfFailed();
                    return TransportFrame.Empty;
                }
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
            lock (_kcpGate)
                _kcp.Dispose();
            try
            {
                _receiveSignal.Release();
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
        }

        internal void ProcessDatagram(ReadOnlySpan<byte> data)
        {
            if (!IsConnected)
                return;

            lock (_kcpGate)
            {
                if (!IsConnected)
                    return;

                _kcp.Input(data);
                if (_kcp.PeekSize() > 0)
                    SignalReceiveData();
            }

            _updateRegistration?.Reschedule(DateTimeOffset.UtcNow);
        }

        private bool TryReadFrame(out TransportFrame frame)
        {
            lock (_kcpGate)
            {
                if (!IsConnected)
                {
                    frame = TransportFrame.Empty;
                    return false;
                }

                if (_accumulator.TryReadFrame(out frame))
                {
                    SignalRemainingReceiveData();
                    return true;
                }

                while (true)
                {
                    var size = _kcp.PeekSize();
                    if (size <= 0)
                        break;

                    if (size > MaxBufferedBytes)
                        throw new InvalidOperationException($"Frame too large: {size} bytes");

                    var payload = ArrayPool<byte>.Shared.Rent(size);
                    try
                    {
                        _kcp.Recv(payload.AsSpan(0, size));
                        _accumulator.Append(payload.AsSpan(0, size));
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(payload);
                    }

                    if (_accumulator.TryReadFrame(out frame))
                    {
                        SignalRemainingReceiveData();
                        return true;
                    }
                }
            }

            frame = TransportFrame.Empty;
            return false;
        }

        private void SignalRemainingReceiveData()
        {
            if (_accumulator.Count > 0 || _kcp.PeekSize() > 0)
                SignalReceiveData();
        }

        private void SignalReceiveData()
        {
            try
            {
                _receiveSignal.Release();
            }
            catch (SemaphoreFullException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private DateTimeOffset UpdateKcp(DateTimeOffset now)
        {
            lock (_kcpGate)
            {
                if (!IsConnected)
                    return DateTimeOffset.MaxValue;

                _kcp.Update(in now);
                return _kcp.Check(in now);
            }
        }

        private void Fail(Exception exception)
        {
            var failure = ExceptionDispatchInfo.Capture(exception);
            if (Interlocked.CompareExchange(ref _terminalFailure, failure, null) is not null)
                return;

            Volatile.Write(ref _isConnected, 0);
            SignalReceiveData();
        }

        private void ThrowIfFailed()
        {
            Volatile.Read(ref _terminalFailure)?.Throw();
        }
    }
}
