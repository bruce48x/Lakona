using System;
using System.Collections.Generic;
using Shared.Contracts.Chat;
using Lakona.Game.Server.Actors;

namespace Server.App.Chat
{
    internal sealed class ChatRoomActor : Actor
    {
        internal const int MaxRecentMessages = 100;
        internal readonly Dictionary<string, ChatRoomMember> Members = new(StringComparer.Ordinal);
        internal readonly Queue<ChatMessage> RecentMessages = new();
    }

    internal sealed record ChatRoomMember(string Name, ILoginCallback LoginCallback, IChatCallback? ChatCallback);

    internal sealed record LoginServiceCall(IActorRuntime Actors, string ConnectionId, ILoginCallback Callback, LoginRequest Request);

    internal sealed record ChatServiceCall(IActorRuntime Actors, string ConnectionId, IChatCallback Callback, ChatBindRequest? BindRequest, ChatSendRequest? SendRequest);
}
