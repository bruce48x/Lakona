using Lakona.Game.Server.ReliablePush;

namespace Lakona.Game.Server.Sessions;

internal sealed class ClientNotifications : IClientNotifications
{
    private readonly IReliablePushRuntime _reliablePush;

    public ClientNotifications(IReliablePushRuntime reliablePush)
    {
        _reliablePush = reliablePush ?? throw new ArgumentNullException(nameof(reliablePush));
    }

    public IClientNotificationTarget ForSession(GameSessionKey session)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(session.OwnerKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(session.SessionId);
        if (session.Generation <= 0)
        {
            throw new ArgumentException("Session generation must be positive.", nameof(session));
        }

        return new Target(this, session);
    }

    private async ValueTask<ClientNotificationStatus> NotifyAsync<TCallback>(
        GameSessionKey session,
        Func<TCallback, ValueTask> notify,
        CancellationToken cancellationToken)
        where TCallback : class
    {
        ArgumentNullException.ThrowIfNull(notify);

        var command = await ClientNotificationCommandFactory
            .CreateAsync(session, notify)
            .ConfigureAwait(false);
        if (command is null)
        {
            return ClientNotificationStatus.Failed;
        }

        return await _reliablePush.PublishAsync(session, command, cancellationToken)
            .ConfigureAwait(false);
    }

    private sealed class Target(ClientNotifications owner, GameSessionKey session) : IClientNotificationTarget
    {
        public ValueTask<ClientNotificationStatus> NotifyAsync<TCallback>(
            Func<TCallback, ValueTask> notify,
            CancellationToken cancellationToken = default)
            where TCallback : class
        {
            return owner.NotifyAsync(
                session,
                notify,
                cancellationToken);
        }
    }
}
