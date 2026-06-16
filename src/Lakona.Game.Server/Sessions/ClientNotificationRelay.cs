namespace Lakona.Game.Server.Sessions;

public sealed class ClientNotificationRelay : IClientNotificationRelay
{
    private readonly IGameSessionDirectory _sessions;

    public ClientNotificationRelay(IGameSessionDirectory sessions)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
    }

    public async ValueTask<ClientNotificationStatus> NotifyAsync<TCallback>(
        GameSessionKey session,
        Action<TCallback> notify,
        CancellationToken cancellationToken = default)
        where TCallback : class
    {
        ArgumentNullException.ThrowIfNull(notify);
        cancellationToken.ThrowIfCancellationRequested();

        var callback = await _sessions.GetCallbackAsync<TCallback>(session, cancellationToken)
            .ConfigureAwait(false);
        if (callback is null)
        {
            return ClientNotificationStatus.RouteNotFound;
        }

        try
        {
            notify(callback);
            return ClientNotificationStatus.Delivered;
        }
        catch
        {
            return ClientNotificationStatus.Failed;
        }
    }
}
