using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Transport.Loopback
{
    public sealed class LoopbackTransport : ITransport
    {
        private const int DefaultQueueCapacity = 256;

        private readonly LoopbackQueue _incoming;
        private readonly LoopbackQueue _outgoing;
        private readonly LoopbackPairState _pair;
        private bool _connected;

        private LoopbackTransport(LoopbackPairState pair, LoopbackQueue incoming, LoopbackQueue outgoing)
        {
            _pair = pair;
            _incoming = incoming;
            _outgoing = outgoing;
        }

        public static void CreatePair(out ITransport client, out ITransport server)
        {
            CreatePair(out client, out server, DefaultQueueCapacity);
        }

        public static void CreatePair(out ITransport client, out ITransport server, int queueCapacity)
        {
            if (queueCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(queueCapacity), "Queue capacity must be positive.");

            var aToB = new LoopbackQueue(queueCapacity);
            var bToA = new LoopbackQueue(queueCapacity);
            var pair = new LoopbackPairState(aToB, bToA);
            client = new LoopbackTransport(pair, bToA, aToB);
            server = new LoopbackTransport(pair, aToB, bToA);
        }

        public bool IsConnected => _connected && !_pair.IsClosed;

        public ValueTask ConnectAsync(CancellationToken ct = default)
        {
            _connected = true;
            return default;
        }

        public async ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Not connected.");

            await _outgoing.WriteAsync(TransportFrame.CopyOf(frame.Span), ct).ConfigureAwait(false);
        }

        public async ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken ct = default)
        {
            if (!_connected)
                throw new InvalidOperationException("Not connected.");

            return await _incoming.ReadAsync(ct).ConfigureAwait(false);
        }

        public ValueTask DisposeAsync()
        {
            _connected = false;
            _pair.Close();
            return default;
        }

        private sealed class LoopbackPairState
        {
            private readonly LoopbackQueue _aToB;
            private readonly LoopbackQueue _bToA;
            private int _closed;

            public LoopbackPairState(LoopbackQueue aToB, LoopbackQueue bToA)
            {
                _aToB = aToB;
                _bToA = bToA;
            }

            public bool IsClosed => Volatile.Read(ref _closed) != 0;

            public void Close()
            {
                if (Interlocked.Exchange(ref _closed, 1) != 0)
                    return;

                _aToB.Complete();
                _bToA.Complete();
                _aToB.Dispose();
                _bToA.Dispose();
            }
        }

        private sealed class LoopbackQueue : IDisposable
        {
            private readonly Channel<TransportFrame> _channel;
            private int _disposed;

            public LoopbackQueue(int capacity)
            {
                _channel = Channel.CreateBounded<TransportFrame>(new BoundedChannelOptions(capacity)
                {
                    AllowSynchronousContinuations = false,
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = false,
                    SingleWriter = false
                });
            }

            public async ValueTask WriteAsync(TransportFrame item, CancellationToken ct)
            {
                try
                {
                    await _channel.Writer.WriteAsync(item, ct).ConfigureAwait(false);
                }
                catch (ChannelClosedException exception)
                {
                    item.Dispose();
                    throw new InvalidOperationException("Loopback queue is completed.", exception);
                }
                catch
                {
                    item.Dispose();
                    throw;
                }
            }

            public async ValueTask<TransportFrame> ReadAsync(CancellationToken ct)
            {
                try
                {
                    return await _channel.Reader.ReadAsync(ct).ConfigureAwait(false);
                }
                catch (ChannelClosedException)
                {
                    return TransportFrame.Empty;
                }
            }

            public void Complete()
            {
                _channel.Writer.TryComplete();
            }

            public void Dispose()
            {
                if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
                {
                    _channel.Writer.TryComplete();
                    while (_channel.Reader.TryRead(out var frame))
                        frame.Dispose();
                }
            }
        }
    }
}
