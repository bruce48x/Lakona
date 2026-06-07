namespace Shared.Contracts.Chat;

public sealed record JoinChatRequest(string UserName);

public sealed record JoinChatResponse(string UserId, IReadOnlyList<ChatMessageDto> RecentMessages);

public sealed record SendChatRequest(string UserId, string Message);

public sealed record SendChatResponse(ChatMessageDto Message);

public sealed record ChatMessageDto(string UserId, string UserName, string Message, DateTimeOffset SentAt);

public sealed record ChatMessagePushed(ChatMessageDto Message);
