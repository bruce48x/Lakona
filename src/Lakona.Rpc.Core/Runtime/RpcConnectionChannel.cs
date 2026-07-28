using System.ComponentModel;

namespace Lakona.Rpc.Core;

/// <summary>
///     Owns serialized frame sending, inbound keepalive handling, activity tracking,
///     and peer-liveness probing for one RPC connection.
/// </summary>
/// <remarks>
///     This is a framework-cooperation type used by the Lakona client and server runtimes.
///     Application code should use generated clients and server hosts.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class RpcConnectionChannel : IDisposable
{
    private readonly RpcKeepAliveOptions _keepAlive;
    private readonly RpcKeepAliveState _keepAliveState;
    private readonly SerializedFrameSender _sender;
    private readonly ITransport _transport;

    public RpcConnectionChannel(ITransport transport, RpcKeepAliveOptions keepAlive)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _keepAlive = keepAlive ?? throw new ArgumentNullException(nameof(keepAlive));
        _keepAliveState = new RpcKeepAliveState(keepAlive.MeasureRtt);
        _sender = new SerializedFrameSender(transport, _keepAliveState);
    }

    public DateTimeOffset LastSendAt => _keepAliveState.LastSendAt;

    public DateTimeOffset LastReceiveAt => _keepAliveState.LastReceiveAt;

    public TimeSpan? LastRtt => _keepAliveState.LastRtt;

    public bool TimedOut => _keepAliveState.TimedOut;

    /// <summary>
    ///     Resets connection activity after a transport has connected or reconnected.
    /// </summary>
    public void ResetActivity()
    {
        _keepAliveState.MarkSent();
        _keepAliveState.MarkReceived();
    }

    /// <summary>
    ///     Sends one frame without allowing concurrent transport writes.
    /// </summary>
    public ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default) =>
        _sender.SendAsync(frame, ct);

    /// <summary>
    ///     Receives the next application frame, handling keepalive ping and pong frames internally.
    /// </summary>
    public async ValueTask<TransportFrame> ReceiveApplicationFrameAsync(CancellationToken ct = default)
    {
        while (true)
        {
            var frame = await _transport.ReceiveFrameAsync(ct).ConfigureAwait(false);
            if (frame.IsEmpty)
                return frame;

            _keepAliveState.MarkReceived();
            RpcFrameType frameType;
            try
            {
                frameType = RpcEnvelopeCodec.PeekFrameType(frame.Span);
            }
            catch
            {
                frame.Dispose();
                throw;
            }

            if (frameType == RpcFrameType.KeepAlivePing)
            {
                using (frame)
                {
                    var ping = RpcEnvelopeCodec.DecodeKeepAlivePing(frame.Span);
                    using var pong = RpcEnvelopeCodec.EncodeKeepAlivePong(new RpcKeepAlivePongEnvelope
                    {
                        TimestampTicksUtc = ping.TimestampTicksUtc
                    });
                    await _sender.SendAsync(pong.Memory, ct).ConfigureAwait(false);
                }

                continue;
            }

            if (frameType == RpcFrameType.KeepAlivePong)
            {
                using (frame)
                {
                    var pong = RpcEnvelopeCodec.DecodeKeepAlivePong(frame.Span);
                    _keepAliveState.RecordPong(pong.TimestampTicksUtc);
                }

                continue;
            }

            return frame;
        }
    }

    /// <summary>
    ///     Runs peer-liveness probing until cancellation, disconnection, or timeout.
    /// </summary>
    public Task RunKeepAliveAsync(
        string timeoutMessage,
        Action<Exception> onTimedOut,
        CancellationToken ct)
    {
        var coordinator = new RpcKeepAliveCoordinator(
            _transport,
            _sender,
            _keepAliveState,
            _keepAlive,
            timeoutMessage,
            onTimedOut,
            markTimedOut: true);
        return coordinator.RunAsync(ct);
    }

    public void Dispose()
    {
        _sender.Dispose();
    }
}
