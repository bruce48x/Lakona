using System.Buffers;
using System.Net.WebSockets;
using Lakona.Rpc.Core;
using NetWebSocket = System.Net.WebSockets.WebSocket;

namespace Lakona.Rpc.Transport.WebSocket;

internal sealed class WsTransportFraming
{
    private static readonly TimeSpan CloseHandshakeTimeout = TimeSpan.FromSeconds(1);
    private readonly NetWebSocket _webSocket;
    private readonly Action? _onDispose;
    private readonly LengthPrefixedFrameAccumulator _accumulator = new();
    private readonly object _gate = new();
    private bool _receiving;
    private bool _stopping;
    private TaskCompletionSource<bool>? _receiveExited;
    private Task? _disposal;

    public WsTransportFraming(NetWebSocket webSocket, Action? onDispose = null)
    {
        _webSocket = webSocket;
        _onDispose = onDispose;
    }

    public bool IsStopping => Volatile.Read(ref _stopping);

    public static async ValueTask SendFrameAsync(NetWebSocket webSocket, ReadOnlyMemory<byte> frame, CancellationToken ct)
    {
        if (webSocket.State != WebSocketState.Open)
            throw new InvalidOperationException("Not connected.");

        using var packed = LengthPrefix.Pack(frame.Span);
        await webSocket.SendAsync(packed.Memory, WebSocketMessageType.Binary, true, ct).ConfigureAwait(false);
    }

    public async ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            if (_stopping) throw new ObjectDisposedException(nameof(WsTransportFraming));
            if (_receiving) throw new InvalidOperationException("Only one receive may be active.");
            _receiving = true;
        }
        try
        {
            if (_webSocket.State != WebSocketState.Open)
                throw new InvalidOperationException("Not connected.");

            while (true)
            {
                // One socket read may contain several RPC frames. Deliver buffered
                // frames before waiting for more input (including the peer's close).
                if (_accumulator.TryReadFrame(out var payload))
                    return payload;

                var tmp = ArrayPool<byte>.Shared.Rent(8 * 1024);
                try
                {
                    // Finish and drain a maximum-sized frame before reading bytes
                    // belonging to its successor into the same bounded accumulator.
                    var remaining = RpcProtocolLimits.DefaultMaxLengthPrefixedFrameSize - _accumulator.Count;
                    var res = await _webSocket.ReceiveAsync(
                        tmp.AsMemory(0, Math.Min(tmp.Length, remaining)), ct).ConfigureAwait(false);
                    if (res.MessageType == WebSocketMessageType.Close)
                        throw new IOException("WebSocket closed.");

                    _accumulator.Append(tmp.AsSpan(0, res.Count));
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(tmp);
                }
            }
        }
        finally
        {
            lock (_gate)
            {
                _receiving = false;
                // No socket buffer or accumulator access is permitted after this signal.
                _receiveExited?.TrySetResult(true);
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        TaskCompletionSource<bool> completion;
        Task receiveExited;
        bool abort;
        lock (_gate)
        {
            if (_disposal is not null) return new ValueTask(_disposal);
            _stopping = true;
            completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _disposal = completion.Task;
            abort = _receiving;
            receiveExited = abort
                ? (_receiveExited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)).Task
                : Task.CompletedTask;
        }
        // Publish the shared completion before invoking socket or owner callbacks.
        _ = DisposeCoreAsync(abort, receiveExited, completion);
        return new ValueTask(completion.Task);
    }

    private async Task DisposeCoreAsync(bool abort, Task receiveExited, TaskCompletionSource<bool> completion)
    {
        try
        {
            try
            {
                try
                {
                    // CloseAsync may receive the peer's close reply. Do not overlap it
                    // with an application receive or clear storage still used by that receive.
                    if (abort) _webSocket.Abort();
                }
                finally
                {
                    await receiveExited.ConfigureAwait(false);
                    _accumulator.Clear();
                    await CloseSocketAsync(_webSocket).ConfigureAwait(false);
                }
            }
            finally { _onDispose?.Invoke(); }
            completion.TrySetResult(true);
        }
        catch (Exception error) { completion.TrySetException(error); }
    }

    private static async ValueTask CloseSocketAsync(NetWebSocket webSocket)
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
