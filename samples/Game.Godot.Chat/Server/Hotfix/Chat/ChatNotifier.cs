using Lakona.Game.Server.Sessions;
using Shared.Contracts.Chat;

namespace Server.Hotfix.Chat;

internal sealed class ChatNotifier
{
    private readonly IClientNotifications _notifications;

    public ChatNotifier(IClientNotifications notifications)
    {
        _notifications = notifications;
    }

    public ValueTask UserJoinedAsync(
        IReadOnlyList<GameSessionKey> recipients,
        ChatMember member,
        CancellationToken cancellationToken = default)
    {
        return PublishAsync<ILoginCallback>(
            recipients,
            callback =>
            {
                callback.OnUserJoined(member);
                return default;
            },
            cancellationToken);
    }

    public ValueTask UserLeftAsync(
        IReadOnlyList<GameSessionKey> recipients,
        string name,
        CancellationToken cancellationToken = default)
    {
        return PublishAsync<ILoginCallback>(
            recipients,
            callback =>
            {
                callback.OnUserLeft(new ChatUserLeft { Name = name });
                return default;
            },
            cancellationToken);
    }

    public ValueTask MessageAsync(
        IReadOnlyList<GameSessionKey> recipients,
        ChatMessage message,
        CancellationToken cancellationToken = default)
    {
        return PublishAsync<IChatCallback>(
            recipients,
            callback =>
            {
                callback.OnMessageReceived(message);
                return default;
            },
            cancellationToken);
    }

    private async ValueTask PublishAsync<TCallback>(
        IReadOnlyList<GameSessionKey> recipients,
        Func<TCallback, ValueTask> notify,
        CancellationToken cancellationToken)
        where TCallback : class
    {
        foreach (var recipient in recipients)
        {
            await _notifications.ForSession(recipient)
                .NotifyAsync(notify, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
