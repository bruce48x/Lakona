namespace Lakona.Game.Server.Sessions;

internal sealed class ClientSessionRouteLifecycleHandler : IGameSessionLifecycleHandler
{
    private const string ControlSessionKind = "control";

    private readonly IClientSessionRouteRegistrar _registrar;
    private readonly IClientSessionIndex _index;

    public ClientSessionRouteLifecycleHandler(
        IClientSessionRouteRegistrar registrar,
        IClientSessionIndex index)
    {
        _registrar = registrar ?? throw new ArgumentNullException(nameof(registrar));
        _index = index ?? throw new ArgumentNullException(nameof(index));
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
        return OnSessionBoundCoreAsync(context, cancellationToken);
    }

    public ValueTask OnSessionDisconnectedAsync(
        GameSessionBindingContext context,
        CancellationToken cancellationToken = default)
    {
        return RemoveIndexAsync(context.Session, cancellationToken);
    }

    public ValueTask OnSessionExpiredAsync(
        GameSessionBindingContext context,
        CancellationToken cancellationToken = default)
    {
        return OnSessionRemovedCoreAsync(context.Session, cancellationToken);
    }

    public ValueTask OnSessionTerminatedAsync(
        GameSessionTerminationContext context,
        CancellationToken cancellationToken = default)
    {
        return OnSessionRemovedCoreAsync(context.Session, cancellationToken);
    }

    private async ValueTask OnSessionBoundCoreAsync(
        GameSessionBindingContext context,
        CancellationToken cancellationToken)
    {
        await _registrar.RegisterAsync(context.Session, cancellationToken).ConfigureAwait(false);
        await _index.UpdateAsync(
            context.Session.OwnerKey,
            ControlSessionKind,
            context.Session,
            context.Session.Generation,
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask OnSessionRemovedCoreAsync(
        GameSessionKey session,
        CancellationToken cancellationToken)
    {
        await _registrar.RemoveAsync(session, cancellationToken).ConfigureAwait(false);
        await RemoveIndexAsync(session, cancellationToken).ConfigureAwait(false);
    }

    private ValueTask RemoveIndexAsync(
        GameSessionKey session,
        CancellationToken cancellationToken)
    {
        return _index.RemoveAsync(
            session.OwnerKey,
            ControlSessionKind,
            session,
            session.Generation,
            cancellationToken);
    }
}
