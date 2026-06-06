using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Transport.Tcp
{
    public sealed class TcpTransport : ITransport
    {
        private const int MaxFrameSize = RpcProtocolLimits.DefaultMaxTransportFrameSize;

        private readonly string _host;
        private readonly int _port;
        private TcpClient? _client;
        private TcpPipeFraming? _framing;
        private NetworkStream? _stream;
        private bool _connected;

        public TcpTransport(string host, int port)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _port = port;
        }

        public bool IsConnected => _connected && _client is not null && _client.Connected;

        public async ValueTask ConnectAsync(CancellationToken ct = default)
        {
            if (_stream is not null)
                return;

            var client = new TcpClient();
            try
            {
#if NET10_0_OR_GREATER || NET8_0_OR_GREATER
                await client.ConnectAsync(_host, _port, ct).ConfigureAwait(false);
#else
                using var registration = ct.Register(static state =>
                {
                    try
                    {
                        ((TcpClient)state!).Dispose();
                    }
                    catch
                    {
                    }
                }, client);

                try
                {
                    await client.ConnectAsync(_host, _port).ConfigureAwait(false);
                }
                catch (ObjectDisposedException) when (ct.IsCancellationRequested)
                {
                    throw new OperationCanceledException(ct);
                }
                catch (SocketException) when (ct.IsCancellationRequested)
                {
                    throw new OperationCanceledException(ct);
                }
#endif
                _stream = client.GetStream();
                _framing = new TcpPipeFraming(_stream, MaxFrameSize);
                _client = client;
                _connected = true;
            }
            catch
            {
                client.Dispose();
                throw;
            }
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
                _client?.Dispose();
            }
            catch
            {
            }

            _framing = null;
            _stream = null;
            _client = null;
        }
    }
}
