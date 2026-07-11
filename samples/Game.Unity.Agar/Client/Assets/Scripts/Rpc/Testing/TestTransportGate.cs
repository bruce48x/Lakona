#nullable enable
#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lakona.Rpc.Core;

namespace Rpc.Testing
{
    public sealed class TestTransportGate
    {
        private readonly object _gate = new object();
        private readonly HashSet<GatedTransport> _active = new HashSet<GatedTransport>();
        private bool _open = true;

        public ITransport Wrap(ITransport inner) => new GatedTransport(this, inner);

        public async Task SetOpenAsync(bool open)
        {
            GatedTransport[] active;
            lock (_gate)
            {
                _open = open;
                active = open ? Array.Empty<GatedTransport>() : new List<GatedTransport>(_active).ToArray();
            }
            foreach (var transport in active) await transport.DisposeAsync();
        }

        private bool IsOpen { get { lock (_gate) return _open; } }
        private void Add(GatedTransport transport) { lock (_gate) _active.Add(transport); }
        private void Remove(GatedTransport transport) { lock (_gate) _active.Remove(transport); }

        private sealed class GatedTransport : ITransport
        {
            private readonly TestTransportGate _owner;
            private readonly ITransport _inner;
            public GatedTransport(TestTransportGate owner, ITransport inner) { _owner = owner; _inner = inner; }
            public bool IsConnected => _inner.IsConnected;
            public async ValueTask ConnectAsync(CancellationToken ct = default)
            {
                if (!_owner.IsOpen) throw new InvalidOperationException("The test network gate is closed.");
                await _inner.ConnectAsync(ct);
                _owner.Add(this);
            }
            public ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default) => _inner.SendFrameAsync(frame, ct);
            public ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken ct = default) => _inner.ReceiveFrameAsync(ct);
            public async ValueTask DisposeAsync() { _owner.Remove(this); await _inner.DisposeAsync(); }
        }
    }
}
#endif
