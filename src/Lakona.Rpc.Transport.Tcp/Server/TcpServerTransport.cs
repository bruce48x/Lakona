using System.Net;
using System.Net.Sockets;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Transport.Tcp
{
    /// <summary>
    ///     ITransport implementation that wraps an accepted TcpClient (server side).
    ///     Uses the same length-prefix framing (4-byte big-endian + payload) as the Unity TcpTransport.
    /// </summary>
    public sealed class TcpServerTransport : ITransport, IRemoteEndPointProvider
    {
        private const int MaxFrameSize = RpcProtocolLimits.DefaultMaxTransportFrameSize;

        private readonly TcpClient _client;
        private bool _connected;
        private TcpPipeFraming? _framing;
        private NetworkStream? _stream;

        public TcpServerTransport(TcpClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _connected = client.Connected;
        }

        public EndPoint? RemoteEndPoint => _client.Client.RemoteEndPoint;

        public bool IsConnected => _connected && _client.Connected;

        public ValueTask ConnectAsync(CancellationToken ct = default)
        {
            if (_stream is not null)
                return default;

            _stream = _client.GetStream();
            _framing = new TcpPipeFraming(_stream, MaxFrameSize);
            _connected = true;
            return default;
        }

        public async ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
        {
            if (_framing is null)
                throw new InvalidOperationException("Not connected.");

            await _framing.SendFrameAsync(frame, ct).ConfigureAwait(false);
        }

        public async ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken ct = default)
        {
            if (_framing is null)
                throw new InvalidOperationException("Not connected.");

            return await _framing.ReceiveFrameAsync(ct).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            _connected = false;
            try
            {
                if (_framing is not null)
                    await _framing.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
            }

            try
            {
                _stream?.Dispose();
            }
            catch
            {
            }

            try
            {
                _client.Dispose();
            }
            catch
            {
            }

            _framing = null;
            _stream = null;
        }
    }
}
