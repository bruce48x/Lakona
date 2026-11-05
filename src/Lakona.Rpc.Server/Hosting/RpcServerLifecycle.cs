namespace Lakona.Rpc.Server;

/// <summary>
///     Describes an RPC server listener that is ready to accept connections.
/// </summary>
/// <param name="ListenAddress">Transport-specific address reported by the connection acceptor.</param>
public sealed record RpcServerListeningContext(string ListenAddress);

/// <summary>
///     Observes RPC server host lifecycle transitions needed by framework hosting integrations.
/// </summary>
public interface IRpcServerLifecycleObserver
{
    /// <summary>
    ///     Runs after the connection acceptor has been created and before the host dispatches accepted connections.
    /// </summary>
    /// <param name="context">Ready listener information.</param>
    /// <param name="cancellationToken">Host cancellation token.</param>
    /// <returns>A task that completes when the observer has accepted the transition.</returns>
    ValueTask OnListeningAsync(
        RpcServerListeningContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Runs when the transport stops accepting connections unexpectedly, before listener
    ///     disposal and Session cleanup. The first accept can fail before the listening notification.
    /// </summary>
    /// <param name="context">Address of the failed listener.</param>
    /// <param name="exception">The original accept failure.</param>
    void OnListenerFailed(RpcServerListeningContext context, Exception exception) { }
}
