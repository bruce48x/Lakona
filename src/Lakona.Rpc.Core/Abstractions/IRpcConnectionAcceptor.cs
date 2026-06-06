namespace Lakona.Rpc.Core;

/// <summary>
///     Server-side transport acceptor that yields accepted RPC connections.
/// </summary>
/// <remarks>
///     Concrete transports such as TCP, WebSocket, and KCP implement this interface. The server host owns
///     the accept loop and creates one <c>RpcSession</c> per accepted connection.
/// </remarks>
public interface IRpcConnectionAcceptor : IAsyncDisposable
{
    /// <summary>
    ///     Human-readable listen address for logs and diagnostics.
    /// </summary>
    string ListenAddress { get; }

    /// <summary>
    ///     Waits for the next accepted connection.
    /// </summary>
    /// <param name="ct">Cancellation token for the accept operation.</param>
    /// <returns>The accepted transport plus display and remote endpoint metadata.</returns>
    ValueTask<RpcAcceptedConnection> AcceptAsync(CancellationToken ct = default);
}
