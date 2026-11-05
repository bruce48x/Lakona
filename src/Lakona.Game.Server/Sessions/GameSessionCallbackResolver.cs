namespace Lakona.Game.Server.Sessions;

internal sealed class GameSessionCallbackResolver
{
    private readonly IGameSessionRegistry sessions;
    private readonly GameFrameworkConnectionRegistry connections;
    private readonly GameSessionCallbackProxyRegistry callbackProxies;

    internal GameSessionCallbackResolver(
        IGameSessionRegistry sessions,
        GameFrameworkConnectionRegistry connections,
        GameSessionCallbackProxyRegistry callbackProxies)
    {
        this.sessions = sessions;
        this.connections = connections;
        this.callbackProxies = callbackProxies;
    }
    public async ValueTask<TCallback?> ResolveAsync<TCallback>(
        GameSessionKey session,
        CancellationToken cancellationToken = default)
        where TCallback : class
    {
        return await ResolveAsync(typeof(TCallback), session, cancellationToken).ConfigureAwait(false)
            as TCallback;
    }

    public async ValueTask<object?> ResolveAsync(
        Type callbackContractType,
        GameSessionKey session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callbackContractType);
        var connectionId = await sessions.GetConnectionIdAsync(session, cancellationToken)
            .ConfigureAwait(false);
        return connectionId is null ? null : Resolve(callbackContractType, connectionId);
    }

    internal object? Resolve(Type callbackContractType, string connectionId) =>
        connections.Get(connectionId) is { } connection ? callbackProxies.TryCreate(callbackContractType, connection) : null;
}
