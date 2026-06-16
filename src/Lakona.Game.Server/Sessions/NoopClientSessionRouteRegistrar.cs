namespace Lakona.Game.Server.Sessions;

internal sealed class NoopClientSessionRouteRegistrar : IClientSessionRouteRegistrar
{
    public ValueTask RegisterAsync(
        GameSessionKey session,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return default;
    }

    public ValueTask RemoveAsync(
        GameSessionKey session,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return default;
    }
}
