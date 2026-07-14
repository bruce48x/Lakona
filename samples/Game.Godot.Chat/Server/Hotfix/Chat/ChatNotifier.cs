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
        return UserJoinedCoreAsync(recipients, member, cancellationToken);
    }

    public ValueTask UserLeftAsync(
        IReadOnlyList<GameSessionKey> recipients,
        string name,
        CancellationToken cancellationToken = default)
    {
        return UserLeftCoreAsync(recipients, new ChatUserLeft { Name = name }, cancellationToken);
    }

    public ValueTask MessageAsync(
        IReadOnlyList<GameSessionKey> recipients,
        ChatMessage message,
        CancellationToken cancellationToken = default)
    {
        return MessageCoreAsync(recipients, message, cancellationToken);
    }

    private async ValueTask UserJoinedCoreAsync(
        IReadOnlyList<GameSessionKey> recipients,
        ChatMember member,
        CancellationToken cancellationToken)
    {
        foreach (var recipient in recipients)
        {
            await _notifications.ForSession<ILoginCallback>(recipient)
                .OnUserJoined(member, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async ValueTask UserLeftCoreAsync(
        IReadOnlyList<GameSessionKey> recipients,
        ChatUserLeft left,
        CancellationToken cancellationToken)
    {
        foreach (var recipient in recipients)
        {
            await _notifications.ForSession<ILoginCallback>(recipient)
                .OnUserLeft(left, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async ValueTask MessageCoreAsync(
        IReadOnlyList<GameSessionKey> recipients,
        ChatMessage message,
        CancellationToken cancellationToken)
    {
        foreach (var recipient in recipients)
        {
            await _notifications.ForSession<IChatCallback>(recipient)
                .OnMessageReceived(message, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
