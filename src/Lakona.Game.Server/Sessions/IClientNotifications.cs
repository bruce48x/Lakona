namespace Lakona.Game.Server.Sessions;

public interface IClientNotifications
{
    IClientNotificationTarget ForSession(GameSessionKey session);
}

public interface IClientNotificationTarget
{
    ValueTask<ClientNotificationStatus> NotifyAsync<TCallback>(
        Func<TCallback, ValueTask> notify,
        CancellationToken cancellationToken = default)
        where TCallback : class;
}

public interface IClientNotificationSink<in TPayload>
{
    ValueTask OnNotificationAsync(TPayload payload, CancellationToken cancellationToken = default);
}
