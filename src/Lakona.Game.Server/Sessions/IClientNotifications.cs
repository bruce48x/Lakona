namespace Lakona.Game.Server.Sessions;

public interface IClientNotifications
{
    IClientNotificationTarget ForUser(string userId);
}

public interface IClientNotificationTarget
{
    ValueTask PublishAsync<TPayload>(
        TPayload payload,
        CancellationToken cancellationToken = default);

    ValueTask PublishReliableAsync<TPayload>(
        string kind,
        TPayload payload,
        CancellationToken cancellationToken = default);
}

public interface IClientNotificationSink<in TPayload>
{
    ValueTask OnNotificationAsync(TPayload payload, CancellationToken cancellationToken = default);
}
