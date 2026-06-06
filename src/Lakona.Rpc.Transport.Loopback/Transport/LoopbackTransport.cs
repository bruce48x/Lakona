using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Transport.Loopback
{
    public sealed class LoopbackTransport : ITransport
    {
        private readonly LoopbackQueue _incoming;
        private readonly LoopbackQueue _outgoing;
        private bool _connected;

        private LoopbackTransport(LoopbackQueue incoming, LoopbackQueue outgoing)
        {
            _incoming = incoming;
            _outgoing = outgoing;
        }

        public static void CreatePair(out ITransport client, out ITransport server)
        {
            var aToB = new LoopbackQueue();
            var bToA = new LoopbackQueue();
            client = new LoopbackTransport(bToA, aToB);
            server = new LoopbackTransport(aToB, bToA);
        }

        public bool IsConnected => _connected;

        public ValueTask ConnectAsync(CancellationToken ct = default)
        {
            _connected = true;
            return default;
        }

        public async ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
        {
            if (!_connected)
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
            _outgoing.Complete();
            _outgoing.Dispose();
            _incoming.Dispose();
            return default;
        }

        private sealed class LoopbackQueue : IDisposable
        {
            private readonly ConcurrentQueue<TransportFrame> _queue = new();
            private readonly SemaphoreSlim _signal = new(0);
            private volatile bool _completed;
            private int _disposed;

            public ValueTask WriteAsync(TransportFrame item, CancellationToken ct)
            {
                if (_completed)
                {
                    item.Dispose();
                    throw new InvalidOperationException("Loopback queue is completed.");
                }

                ct.ThrowIfCancellationRequested();
                _queue.Enqueue(item);
                try { _signal.Release(); } catch (ObjectDisposedException) { }
                return default;
            }

            public async ValueTask<TransportFrame> ReadAsync(CancellationToken ct)
            {
                while (true)
                {
                    if (_queue.TryDequeue(out var item))
                        return item;

                    if (_completed)
                        return TransportFrame.Empty;

                    try
                    {
                        await _signal.WaitAsync(ct).ConfigureAwait(false);
                    }
                    catch (ObjectDisposedException)
                    {
                        return TransportFrame.Empty;
                    }
                }
            }

            public void Complete()
            {
                _completed = true;
                try { _signal.Release(); } catch (ObjectDisposedException) { }
            }

            public void Dispose()
            {
                if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
                {
                    while (_queue.TryDequeue(out var frame))
                        frame.Dispose();

                    _signal.Dispose();
                }
            }
        }
    }
}
