namespace Lakona.Game.Client
{
    /// <summary>The locally observed connection lifecycle of a generated Game client, independent of its session phase.</summary>
    public enum LakonaGameConnectionState
    {
        /// <summary>The client has not started connecting.</summary>
        Created,
        /// <summary>The initial connection and Game handshake are in progress.</summary>
        Connecting,
        /// <summary>The current connection completed Game initialization or recovery confirmation. This does not imply business login or state synchronization.</summary>
        Connected,
        /// <summary>Recovery is pending or in progress, including draining the stopped connection, backoff and recovery confirmation.</summary>
        Reconnecting,
        /// <summary>The connection is unavailable and automatic recovery has ended.</summary>
        Disconnected,
        /// <summary>Disposal has started. Await DisposeAsync to observe cleanup completion.</summary>
        Disposed
    }
}
