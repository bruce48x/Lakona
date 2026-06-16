namespace Lakona.Game.Server.Sessions;

internal sealed class ClientSessionRouteLifecycleHandler : IGameSessionLifecycleHandler
{
    private readonly IClientSessionRouteRegistrar _registrar;

    public ClientSessionRouteLifecycleHandler(IClientSessionRouteRegistrar registrar)
    {
        _registrar = registrar ?? throw new ArgumentNullException(nameof(registrar));
    }

    public ValueTask OnConnectionOpenedAsync(
        GameConnectionContext context,
        CancellationToken cancellationToken = default)
    {
        return default;
    }

    public ValueTask OnSessionBoundAsync(
        GameSessionBindingContext context,
        CancellationToken cancellationToken = default)
    {
        return _registrar.RegisterAsync(context.Session, cancellationToken);
    }

    public ValueTask OnSessionDisconnectedAsync(
        GameSessionBindingContext context,
        CancellationToken cancellationToken = default)
    {
        return default;
    }

    public ValueTask OnSessionExpiredAsync(
        GameSessionBindingContext context,
        CancellationToken cancellationToken = default)
    {
        return _registrar.RemoveAsync(context.Session, cancellationToken);
    }

    public ValueTask OnSessionTerminatedAsync(
        GameSessionTerminationContext context,
        CancellationToken cancellationToken = default)
    {
        return _registrar.RemoveAsync(context.Session, cancellationToken);
    }
}
