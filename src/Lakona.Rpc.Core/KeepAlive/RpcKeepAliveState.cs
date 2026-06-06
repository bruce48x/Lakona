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
    private long _pendingPingSentAtTicksUtc;

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
        ClearPendingPing();
    }

    public void MarkPingSent(long pingTimestampTicksUtc)
    {
        Volatile.Write(ref _pendingPingSentAtTicksUtc, pingTimestampTicksUtc);
    }

    public void MarkTimedOut()
    {
        Volatile.Write(ref _timedOut, 1);
    }

    public RpcKeepAliveAction GetNextAction(long nowTicksUtc, TimeSpan interval, TimeSpan timeout)
    {
        var pendingPingSentAt = Volatile.Read(ref _pendingPingSentAtTicksUtc);
        if (pendingPingSentAt > 0)
            return new TimeSpan(nowTicksUtc - pendingPingSentAt) >= timeout
                ? RpcKeepAliveAction.TimedOut
                : RpcKeepAliveAction.None;

        var lastReceive = Volatile.Read(ref _lastReceiveTicksUtc);
        return new TimeSpan(nowTicksUtc - lastReceive) >= interval
            ? RpcKeepAliveAction.SendPing
            : RpcKeepAliveAction.None;
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

    private void ClearPendingPing()
    {
        Volatile.Write(ref _pendingPingSentAtTicksUtc, 0);
    }

    private static long GetTimestampOrNow(long utcTicks) =>
        utcTicks > 0 ? utcTicks : DateTimeOffset.UtcNow.UtcTicks;
}
