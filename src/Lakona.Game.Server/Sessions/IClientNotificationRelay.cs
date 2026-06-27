namespace Lakona.Game.Server.Sessions;

internal interface IClientNotificationRelay
{
    ValueTask<ClientNotificationStatus> NotifyAsync<TCallback>(
        GameSessionKey session,
        Func<TCallback, ValueTask> notify,
        CancellationToken cancellationToken = default)
        where TCallback : class;
}
