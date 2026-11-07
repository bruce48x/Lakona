using System.Net;
using Lakona.Rpc.Core;
using NetWebSocket = System.Net.WebSockets.WebSocket;

namespace Lakona.Rpc.Transport.WebSocket;

public sealed class WsServerTransport : ITransport, IRemoteEndPointProvider
{
    private readonly CancellationToken _disconnectToken;
    private readonly NetWebSocket _webSocket;
    private readonly WsTransportFraming _framing;

    public WsServerTransport(
        NetWebSocket webSocket,
        EndPoint? remoteEndPoint = null,
        Action? onDispose = null,
        CancellationToken disconnectToken = default)
    {
        _webSocket = webSocket ?? throw new ArgumentNullException(nameof(webSocket));
        RemoteEndPoint = remoteEndPoint;
        _framing = new WsTransportFraming(_webSocket, onDispose);
        _disconnectToken = disconnectToken;
    }

    public EndPoint? RemoteEndPoint { get; }

    public bool IsConnected =>
        !_framing.IsStopping && !_disconnectToken.IsCancellationRequested &&
        _webSocket.State == System.Net.WebSockets.WebSocketState.Open;

    public ValueTask ConnectAsync(CancellationToken ct = default)
    {
        return default;
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
