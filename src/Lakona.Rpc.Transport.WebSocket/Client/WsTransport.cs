using System.Net.WebSockets;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Transport.WebSocket;

public sealed class WsTransport : ITransport
{
    private readonly Uri _uri;
    private readonly ClientWebSocket _webSocket = new();
    private readonly WsTransportFraming _framing;

    public WsTransport(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("WebSocket URL is required.", nameof(url));

        _uri = new Uri(url, UriKind.Absolute);
        _framing = new WsTransportFraming(_webSocket);
    }

    public WsTransport(Uri uri)
    {
        _uri = uri ?? throw new ArgumentNullException(nameof(uri));
        _framing = new WsTransportFraming(_webSocket);
    }

    public bool IsConnected => !_framing.IsStopping && _webSocket.State == WebSocketState.Open;

    public async ValueTask ConnectAsync(CancellationToken ct = default)
    {
        if (_webSocket.State == WebSocketState.Open)
            return;

        await _webSocket.ConnectAsync(_uri, ct).ConfigureAwait(false);
    }

    public ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
    {
        return WsTransportFraming.SendFrameAsync(_webSocket, frame, ct);
    }

    public ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken ct = default)
    {
        return _framing.ReceiveFrameAsync(ct);
    }

    public ValueTask DisposeAsync()
    {
        return _framing.DisposeAsync();
    }
}
