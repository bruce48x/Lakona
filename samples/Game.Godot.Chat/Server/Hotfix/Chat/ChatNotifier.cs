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

    public void UserJoined(
        IReadOnlyList<GameSessionKey> recipients,
        ChatMember member)
    {
        foreach (var recipient in recipients)
        {
            _notifications.ForSession<ILoginCallback>(recipient).OnUserJoined(member);
        }
    }

    public void UserLeft(
        IReadOnlyList<GameSessionKey> recipients,
        string name)
    {
        var left = new ChatUserLeft { Name = name };
        foreach (var recipient in recipients)
        {
            _notifications.ForSession<ILoginCallback>(recipient).OnUserLeft(left);
        }
    }

    public void Message(
        IReadOnlyList<GameSessionKey> recipients,
        ChatMessage message)
    {
        foreach (var recipient in recipients)
        {
            _notifications.ForSession<IChatCallback>(recipient).OnMessageReceived(message);
        }
    }
}
