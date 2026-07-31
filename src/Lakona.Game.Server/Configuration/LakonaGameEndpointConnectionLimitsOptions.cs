namespace Lakona.Game.Server.Configuration;

/// <summary>
/// Bounds connection resources owned by one client-facing game endpoint.
/// </summary>
public sealed class LakonaGameEndpointConnectionLimitsOptions
{
    /// <summary>Default active RPC connection capacity for one endpoint.</summary>
    public const int DefaultMaxActiveConnections = 10_000;
    /// <summary>Default pre-handshake RPC connection capacity for one endpoint.</summary>
    public const int DefaultMaxPendingHandshakes = 1_000;
    /// <summary>Default deadline for completing the Game Handshake.</summary>
    public static readonly TimeSpan DefaultHandshakeTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Gets the hard maximum number of active RPC connections.</summary>
    public int MaxActiveConnections { get; init; } = DefaultMaxActiveConnections;

    /// <summary>Gets the maximum active connections that have not completed the Game Handshake.</summary>
    public int MaxPendingHandshakes { get; init; } = DefaultMaxPendingHandshakes;

    /// <summary>Gets the deadline from RPC Session admission to successful Game Handshake completion.</summary>
    public TimeSpan HandshakeTimeout { get; init; } = DefaultHandshakeTimeout;
}
