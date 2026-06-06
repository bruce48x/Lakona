using System.Buffers;
using System.Net.WebSockets;
using Lakona.Rpc.Core;
using NetWebSocket = System.Net.WebSockets.WebSocket;

namespace Lakona.Rpc.Transport.WebSocket;

internal static class WsTransportFraming
{
    private const int MaxBufferedBytes = RpcProtocolLimits.DefaultMaxTransportFrameSize;
    private static readonly TimeSpan CloseHandshakeTimeout = TimeSpan.FromSeconds(1);

    public static async ValueTask SendFrameAsync(NetWebSocket webSocket, ReadOnlyMemory<byte> frame, CancellationToken ct)
    {
        if (webSocket.State != WebSocketState.Open)
            throw new InvalidOperationException("Not connected.");

        using var packed = LengthPrefix.Pack(frame.Span);
        await webSocket.SendAsync(packed.Memory, WebSocketMessageType.Binary, true, ct).ConfigureAwait(false);
    }

    public static async ValueTask<TransportFrame> ReceiveFrameAsync(
        NetWebSocket webSocket,
        LengthPrefixedFrameAccumulator accumulator,
        CancellationToken ct)
    {
        if (webSocket.State != WebSocketState.Open)
            throw new InvalidOperationException("Not connected.");

        while (true)
        {
            var tmp = ArrayPool<byte>.Shared.Rent(8 * 1024);
            try
            {
                var res = await webSocket.ReceiveAsync(tmp, ct).ConfigureAwait(false);
                if (res.MessageType == WebSocketMessageType.Close)
                    throw new IOException("WebSocket closed.");

                accumulator.Append(tmp.AsSpan(0, res.Count), MaxBufferedBytes);

                if (accumulator.TryReadFrame(out var payload))
                    return payload;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(tmp);
            }
        }
    }

    public static async ValueTask DisposeAsync(NetWebSocket webSocket)
    {
        try
        {
            if (webSocket.State == WebSocketState.Open || webSocket.State == WebSocketState.CloseReceived)
            {
                using var closeCts = new CancellationTokenSource(CloseHandshakeTimeout);
                try
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", closeCts.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    webSocket.Abort();
                }
            }
        }
        catch
        {
            try
            {
                webSocket.Abort();
            }
            catch
            {
            }
        }

        webSocket.Dispose();
    }
}
