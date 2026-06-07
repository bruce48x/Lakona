using Shared.Contracts.Chat;
using Lakona.Game.Server.Actors;

namespace Server.Chat;

public sealed class ChatRoomActor : Actor
{
    private readonly Dictionary<string, string> users = new(StringComparer.Ordinal);
    private readonly List<ChatMessageDto> recentMessages = [];

    public Task<JoinChatResponse> JoinAsync(JoinChatRequest request, CancellationToken cancellationToken)
    {
        string userId = Guid.NewGuid().ToString("N");
        users[userId] = request.UserName;
        return Task.FromResult(new JoinChatResponse(userId, recentMessages.ToArray()));
    }

    public Task<SendChatResponse> SendAsync(SendChatRequest request, CancellationToken cancellationToken)
    {
        if (!users.TryGetValue(request.UserId, out string? userName))
        {
            throw new InvalidOperationException("User must join chat before sending messages.");
        }

        string filtered = ChatRules.FilterMessage(request.Message);
        ChatMessageDto message = new(request.UserId, userName, filtered, DateTimeOffset.UtcNow);
        recentMessages.Add(message);

        if (recentMessages.Count > 50)
        {
            recentMessages.RemoveAt(0);
        }

        return Task.FromResult(new SendChatResponse(message));
    }
}
