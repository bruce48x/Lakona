namespace Lakona.Rpc.Server;

public sealed record RpcSessionLifecycleContext(
    string ConnectionId,
    string DisplayName);

public interface IRpcSessionLifecycleObserver
{
    ValueTask OnSessionStartedAsync(
        RpcSessionLifecycleContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Observes the terminal RPC Session state after its transport and admission leases are released
    /// and the host has returned its active-connection capacity.
    /// </summary>
    ValueTask OnSessionDisconnectedAsync(
        RpcSessionLifecycleContext context,
        Exception? error,
        CancellationToken cancellationToken = default);
}
