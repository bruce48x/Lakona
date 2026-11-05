namespace Lakona.Rpc.Core;

internal sealed class RpcKeepAliveCoordinator
{
    private readonly RpcKeepAliveOptions _keepAlive;
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
        Action<Exception> onTimedOut)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _keepAlive = keepAlive ?? throw new ArgumentNullException(nameof(keepAlive));
        _timeoutMessage = timeoutMessage ?? throw new ArgumentNullException(nameof(timeoutMessage));
        _onTimedOut = onTimedOut ?? throw new ArgumentNullException(nameof(onTimedOut));
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var interval = _keepAlive.Interval;
        var timeout = _keepAlive.Timeout;
        if (interval <= TimeSpan.Zero || timeout <= TimeSpan.Zero)
            return;

        _state.BeginProbing();
        try
        {
            await ProbeAsync(interval, timeout, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) when (!_transport.IsConnected) { }
        finally { _state.EndProbing(); }
    }

    private async Task ProbeAsync(TimeSpan interval, TimeSpan timeout, CancellationToken ct)
    {
        using var writeCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task? write = null;
        bool timedOut = false;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (write is { IsCompleted: true })
                {
                    var completed = write;
                    write = null;
                    await completed.ConfigureAwait(false);
                }

                var action = _state.GetNextAction(interval, timeout, write is null, ct, out var delay);
                if (action == RpcKeepAliveAction.TimedOut)
                {
                    timedOut = true;
                    _onTimedOut(new TimeoutException(_timeoutMessage));
                    return;
                }
                if (action == RpcKeepAliveAction.SendPing)
                {
                    // Even the synchronous part of a transport write can stall.
                    write = Task.Run(() => SendPingAsync(writeCancellation.Token));
                }
                await WaitForCheckAsync(delay, write, ct).ConfigureAwait(false);
            }
        }
        catch (AggregateException) when (timedOut && ct.IsCancellationRequested)
        {
            // The owner recorded the timeout before running its I/O cancellation callbacks.
        }
        finally
        {
            if (write is not null)
            {
                try { writeCancellation.Cancel(); }
                catch (AggregateException) when (timedOut)
                {
                    // Cancellation callbacks run after the connection has committed its timeout.
                }
                finally
                {
                    try { await write.ConfigureAwait(false); }
                    catch (Exception) when (timedOut)
                    {
                        // The timed-out write still owns its frame until it completes.
                    }
                }
            }
        }
    }

    private async Task SendPingAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var frame = RpcEnvelopeCodec.EncodeKeepAlivePing(new RpcKeepAlivePingEnvelope
        {
            TimestampTicksUtc = DateTimeOffset.UtcNow.UtcTicks
        });
        await _sender.SendAsync(frame.Memory, ct).ConfigureAwait(false);
    }

    private static async Task WaitForCheckAsync(TimeSpan delay, Task? write, CancellationToken ct)
    {
        // Task.Delay truncates sub-millisecond waits and has a platform-dependent maximum.
        if (delay < TimeSpan.FromMilliseconds(1)) delay = TimeSpan.FromMilliseconds(1);
        if (delay > TimeSpan.FromMilliseconds(int.MaxValue)) delay = TimeSpan.FromMilliseconds(int.MaxValue);
        if (write is null)
        {
            await Task.Delay(delay, ct).ConfigureAwait(false);
            return;
        }
        if (write.IsCompleted) return;
        using var timerCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var timer = Task.Delay(delay, timerCancellation.Token);
        if (await Task.WhenAny(write, timer).ConfigureAwait(false) == write)
        {
            timerCancellation.Cancel();
        }
    }
}
