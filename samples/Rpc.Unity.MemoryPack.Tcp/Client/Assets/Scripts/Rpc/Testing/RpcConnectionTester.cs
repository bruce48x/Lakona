using System;
using System.Threading;
using System.Threading.Tasks;
using Lakona.Rpc.Client;
using Lakona.Rpc.Core;
using Lakona.Rpc.Transport.Tcp;
using UnityEngine;

namespace Rpc.Testing
{
    public class RpcConnectionTester : RpcConnectionTesterBase
    {
        [SerializeField] private RpcEndpointSettings _endpoint = RpcEndpointSettings.CreateTcp("127.0.0.1", 20000);

        protected override RpcEndpointSettings Endpoint => _endpoint;
        protected override string RuntimeTitle => "RpcCall.MemoryPack Runtime";
        protected override RpcTransportKind TransportKind => RpcTransportKind.Tcp;

        protected override RpcClientOptions CreateClientOptions()
        {
            return new RpcClientOptions(
                new SessionTcpTransport(_endpoint.Host, _endpoint.Port),
                new global::Lakona.Rpc.Serializer.MemoryPack.MemoryPackRpcSerializer());
        }

        private sealed class SessionTcpTransport : ITransport
        {
            private readonly TcpTransport _tcp;
            private readonly Lazy<Task> _close;
            private CancellationTokenRegistration _sessionCancellation;

            public SessionTcpTransport(string host, int port)
            {
                _tcp = new TcpTransport(host, port);
                _close = new Lazy<Task>(() => _tcp.DisposeAsync().AsTask());
            }

            public bool IsConnected => _tcp.IsConnected;

            public async ValueTask ConnectAsync(CancellationToken ct = default)
            {
                await _tcp.ConnectAsync(ct).ConfigureAwait(false);
                // The tester passes its session lifetime here. Closing the socket interrupts a pending
                // Mono NetworkStream read so RPC disposal can join the receive loop even without a reply.
                _sessionCancellation = ct.Register(() => { _ = _close.Value; });
                ct.ThrowIfCancellationRequested();
            }

            public ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default) =>
                _tcp.SendFrameAsync(frame, ct);

            public ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken ct = default) =>
                _tcp.ReceiveFrameAsync(ct);

            public ValueTask DisposeAsync()
            {
                _sessionCancellation.Dispose();
                return new ValueTask(_close.Value);
            }
        }
    }
}
