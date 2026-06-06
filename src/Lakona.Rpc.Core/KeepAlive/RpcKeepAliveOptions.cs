namespace Lakona.Rpc.Core;

/// <summary>
///     Peer liveness probing for RPC transports.
///     Only inbound traffic proves the remote peer is alive; outbound sends do not suppress probes.
/// </summary>
public sealed class RpcKeepAliveOptions
{
    /// <summary>
    ///     Shared disabled keepalive configuration.
    /// </summary>
    public static RpcKeepAliveOptions Disabled { get; } = new();

    /// <summary>
    ///     Enables keepalive probing.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    ///     Maximum time without receiving any frame before a keepalive ping is sent.
    /// </summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>
    ///     Maximum time to wait for an inbound frame after a ping before disconnecting the session.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(45);

    /// <summary>
    ///     Measures round-trip time from keepalive ping/pong timestamps when enabled.
    /// </summary>
    public bool MeasureRtt { get; init; } = true;
}
