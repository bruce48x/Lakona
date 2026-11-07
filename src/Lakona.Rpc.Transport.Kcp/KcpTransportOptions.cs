using System.Net.Sockets.Kcp;

namespace Lakona.Rpc.Transport.Kcp;

/// <summary>KCP tuning applied once when a transport or listener is constructed.</summary>
/// <remarks>Options are copied. Each peer configures its own endpoint; values are not negotiated.
/// Message mode, MTU and the bounded pending-send budget are transport invariants.</remarks>
public sealed class KcpTransportOptions
{
    /// <summary>Protocol update interval in milliseconds, from 10 to 5000.</summary>
    public int UpdateIntervalMilliseconds { get; set; } = 20;
    /// <summary>Maximum send window in segments, from 1 to 512.</summary>
    public int SendWindowSize { get; set; } = 128;
    /// <summary>Receive window in segments, from 128 to 65535.</summary>
    public int ReceiveWindowSize { get; set; } = 128;
    /// <summary>Enables KCP's shorter retransmission timeouts.</summary>
    public bool NoDelay { get; set; }
    /// <summary>ACK skips before fast retransmission; zero disables it.</summary>
    public int FastResend { get; set; }
    /// <summary>Whether KCP congestion control is enabled. Defaults to true.</summary>
    public bool CongestionControl { get; set; } = true;

    internal KcpTransportOptions Snapshot()
    {
        var copy = (KcpTransportOptions)MemberwiseClone();
        if (copy.UpdateIntervalMilliseconds < 10 || copy.UpdateIntervalMilliseconds > 5000)
            throw new ArgumentOutOfRangeException(nameof(UpdateIntervalMilliseconds));
        if (copy.SendWindowSize < 1 || copy.SendWindowSize > KcpFrameSender.MaximumPendingSegments)
            throw new ArgumentOutOfRangeException(nameof(SendWindowSize));
        // One 64 KiB KCP message needs 48 fragments at the fixed 1400-byte MTU.
        // Keep the library's minimum receive window and its 16-bit wire field.
        if (copy.ReceiveWindowSize < 128 || copy.ReceiveWindowSize > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(ReceiveWindowSize));
        if (copy.FastResend < 0)
            throw new ArgumentOutOfRangeException(nameof(FastResend));
        return copy;
    }

    internal void Apply(SimpleSegManager.Kcp kcp)
    {
        kcp.NoDelay(NoDelay ? 1 : 0, UpdateIntervalMilliseconds, FastResend, CongestionControl ? 0 : 1);
        kcp.WndSize(SendWindowSize, ReceiveWindowSize);
    }
}
