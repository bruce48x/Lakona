namespace Lakona.Game.Server.Sessions;

internal sealed class ClientNotifications : IClientNotifications
{
    private readonly IClientNotificationCommandRouter _router;

    public ClientNotifications(IClientNotificationCommandRouter router)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
    }

    public ClientNotificationTarget<TCallback> ForSession<TCallback>(GameSessionKey session)
        where TCallback : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(session.OwnerKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(session.SessionId);
        if (session.Generation <= 0)
        {
            throw new ArgumentException("Session generation must be positive.", nameof(session));
        }

        return new ClientNotificationTarget<TCallback>(_router, session);
    }
}
