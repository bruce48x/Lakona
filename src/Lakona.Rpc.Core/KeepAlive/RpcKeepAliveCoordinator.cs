namespace Lakona.Rpc.Core;

internal sealed class RpcKeepAliveCoordinator
{
    private readonly RpcKeepAliveOptions _keepAlive;
    private readonly bool _markTimedOut;
    private readonly Action<Exception> _onTimedOut;
    private readonly SerializedFrameSender _sender;
    private readonly RpcKeepAliveState _state;
    private readonly string _timeoutMessage;
    private readonly ITransport _transport;

    public RpcKeepAliveCoordinator(
        ITransport transport,
        SerializedFrameSender sender,
        RpcKeepAliveState state,
        RpcKeepAliveOptions keepAlive,
        string timeoutMessage,
        Action<Exception> onTimedOut,
        bool markTimedOut)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _keepAlive = keepAlive ?? throw new ArgumentNullException(nameof(keepAlive));
        _timeoutMessage = timeoutMessage ?? throw new ArgumentNullException(nameof(timeoutMessage));
        _onTimedOut = onTimedOut ?? throw new ArgumentNullException(nameof(onTimedOut));
        _markTimedOut = markTimedOut;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var interval = _keepAlive.Interval;
        var timeout = _keepAlive.Timeout;
        if (interval <= TimeSpan.Zero || timeout <= TimeSpan.Zero)
            return;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var nowTicks = DateTimeOffset.UtcNow.UtcTicks;
            switch (_state.GetNextAction(nowTicks, interval, timeout))
            {
                case RpcKeepAliveAction.None:
                    continue;
                case RpcKeepAliveAction.TimedOut:
                    if (_markTimedOut)
                        _state.MarkTimedOut();
                    _onTimedOut(new TimeoutException(_timeoutMessage));
                    return;
                case RpcKeepAliveAction.SendPing:
                    break;
            }

            try
            {
                var pingTimestamp = DateTimeOffset.UtcNow.UtcTicks;
                using var pingBytes = RpcEnvelopeCodec.EncodeKeepAlivePing(new RpcKeepAlivePingEnvelope
                {
                    TimestampTicksUtc = pingTimestamp
                });
                await _sender.SendAsync(pingBytes.Memory, ct).ConfigureAwait(false);
                _state.MarkPingSent(pingTimestamp);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (InvalidOperationException) when (!_transport.IsConnected)
            {
                return;
            }
        }
    }
}
