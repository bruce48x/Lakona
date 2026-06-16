namespace Lakona.Game.Server.Sessions;

public interface IClientNotificationRelay
{
    ValueTask<ClientNotificationStatus> NotifyAsync<TCallback>(
        GameSessionKey session,
        Action<TCallback> notify,
        CancellationToken cancellationToken = default)
        where TCallback : class;
}
