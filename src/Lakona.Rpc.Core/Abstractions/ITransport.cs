namespace Lakona.Rpc.Core
{
    /// <summary>
    ///     Transport boundary for RPC: sends and receives complete frames (one message).
    ///     TCP/WS/KCP differences are hidden below this interface.
    /// </summary>
    public interface ITransport : IAsyncDisposable
    {
        bool IsConnected { get; }

        /// <summary>
        ///     Prepares this transport for frame I/O.
        /// </summary>
        /// <remarks>
        ///     Client transports use this call to actively connect to their remote endpoint.
        ///     Accepted server transports use it to initialize per-connection state such as streams,
        ///     schedulers, or framing over an already accepted connection. In-memory or already-open
        ///     transports may implement it as an idempotent no-op.
        /// </remarks>
        ValueTask ConnectAsync(CancellationToken ct = default);

        /// <summary>
        ///     Sends one complete frame.
        /// </summary>
        /// <param name="frame">Frame bytes to send. The transport must not retain this memory after the call completes.</param>
        /// <param name="ct">Cancellation token for the send operation.</param>
        ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default);

        /// <summary>
        ///     Receives one complete frame.
        /// </summary>
        /// <param name="ct">Cancellation token for the receive operation.</param>
        /// <returns>
        ///     An owned frame. The caller disposes it after processing. An empty frame means the remote side closed
        ///     the connection.
        /// </returns>
        ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken ct = default);
    }
}
