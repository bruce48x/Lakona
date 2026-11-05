using System.Diagnostics;
using System.Threading;

namespace Lakona.Rpc.Core;

internal enum RpcKeepAliveAction
{
    None = 0,
    SendPing = 1,
    TimedOut = 2
}

internal sealed class RpcKeepAliveState
{
    private readonly bool _measureRtt;
    private int _timedOut;
    private long _lastReceiveTicksUtc;
    private long _lastRttTicks;
    private long _lastSendTicksUtc;
    private readonly object _probeGate = new();
    private volatile bool _probing;
    private long _lastReceiveTimestamp;
    private long? _probeStarted;
    private bool _pingSubmitted;

    public RpcKeepAliveState(bool measureRtt)
    {
        _measureRtt = measureRtt;
        MarkSent();
        MarkReceived();
    }

    public DateTimeOffset LastSendAt => new(GetTimestampOrNow(_lastSendTicksUtc), TimeSpan.Zero);

    public DateTimeOffset LastReceiveAt => new(GetTimestampOrNow(_lastReceiveTicksUtc), TimeSpan.Zero);

    public TimeSpan? LastRtt
    {
        get
        {
            var ticks = Volatile.Read(ref _lastRttTicks);
            return ticks <= 0 ? null : TimeSpan.FromTicks(ticks);
        }
    }

    public bool TimedOut => Volatile.Read(ref _timedOut) != 0;

    public void MarkSent()
    {
        Volatile.Write(ref _lastSendTicksUtc, DateTimeOffset.UtcNow.UtcTicks);
    }

    public void MarkReceived()
    {
        Volatile.Write(ref _lastReceiveTicksUtc, DateTimeOffset.UtcNow.UtcTicks);
        if (!_probing) return;
        lock (_probeGate)
        {
            _lastReceiveTimestamp = Stopwatch.GetTimestamp();
            _probeStarted = null;
        }
    }

    public void BeginProbing()
    {
        lock (_probeGate)
        {
            if (_probing) throw new InvalidOperationException("RPC keepalive is already running.");
            _lastReceiveTimestamp = Stopwatch.GetTimestamp();
            _probeStarted = null;
            _probing = true;
        }
    }

    public void EndProbing() => _probing = false;

    public RpcKeepAliveAction GetNextAction(TimeSpan interval, TimeSpan timeout, bool canSend,
        CancellationToken ct, out TimeSpan delay)
    {
        lock (_probeGate)
        {
            ct.ThrowIfCancellationRequested();
            var now = Stopwatch.GetTimestamp();
            if (_probeStarted is null)
            {
                delay = interval - Elapsed(_lastReceiveTimestamp, now);
                if (delay > TimeSpan.Zero) return RpcKeepAliveAction.None;

                // The deadline starts even if a write from the previous probe still owns the sender.
                _probeStarted = now;
                _pingSubmitted = false;
            }

            delay = timeout - Elapsed(_probeStarted.Value, now);
            if (delay <= TimeSpan.Zero)
            {
                // Receiving a frame and committing a timeout compete under the same lock.
                Volatile.Write(ref _timedOut, 1);
                return RpcKeepAliveAction.TimedOut;
            }

            // An inbound frame may clear the probe and start a new idle interval while we wait.
            if (delay > interval) delay = interval;
            if (_pingSubmitted || !canSend) return RpcKeepAliveAction.None;
            _pingSubmitted = true;
            return RpcKeepAliveAction.SendPing;
        }
    }

    public void RecordPong(long pongTimestampTicksUtc)
    {
        if (!_measureRtt)
            return;

        if (pongTimestampTicksUtc <= 0)
            return;

        var nowTicks = DateTimeOffset.UtcNow.UtcTicks;
        if (nowTicks <= pongTimestampTicksUtc)
            return;

        Volatile.Write(ref _lastRttTicks, nowTicks - pongTimestampTicksUtc);
    }

    private static TimeSpan Elapsed(long start, long now) =>
        TimeSpan.FromTicks((long)((now - start) * ((double)TimeSpan.TicksPerSecond / Stopwatch.Frequency)));

    private static long GetTimestampOrNow(long utcTicks) =>
        utcTicks > 0 ? utcTicks : DateTimeOffset.UtcNow.UtcTicks;
}
