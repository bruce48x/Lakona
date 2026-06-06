namespace Shared.Contracts.Chat;

public interface IChatService
{
    Task<JoinChatResponse> JoinAsync(JoinChatRequest request, CancellationToken cancellationToken = default);

    Task<SendChatResponse> SendAsync(SendChatRequest request, CancellationToken cancellationToken = default);
}
