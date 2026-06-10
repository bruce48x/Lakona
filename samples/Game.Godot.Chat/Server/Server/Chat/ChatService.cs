using Shared.Contracts.Chat;
using Lakona.Game.Server.Actors;

namespace Server.Chat;

public sealed class ChatService : IChatService
{
    private static readonly ActorId GlobalChatRoomId = ActorId.From("chat:global");
    private readonly IActorRuntime actorRuntime;

    public ChatService(IActorRuntime actorRuntime)
    {
        this.actorRuntime = actorRuntime;
    }

    public Task<JoinChatResponse> JoinAsync(JoinChatRequest request, CancellationToken cancellationToken = default)
    {
        return actorRuntime.AskAsync<ChatRoomActor, JoinChatResponse>(
            GlobalChatRoomId,
            (actor, ct) => new ValueTask<JoinChatResponse>(actor.JoinAsync(request, ct)),
            cancellationToken).AsTask();
    }

    public Task<SendChatResponse> SendAsync(SendChatRequest request, CancellationToken cancellationToken = default)
    {
        return actorRuntime.AskAsync<ChatRoomActor, SendChatResponse>(
            GlobalChatRoomId,
            (actor, ct) => new ValueTask<SendChatResponse>(actor.SendAsync(request, ct)),
            cancellationToken).AsTask();
    }
}
