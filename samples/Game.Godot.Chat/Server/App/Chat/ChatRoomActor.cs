using System;
using System.Collections.Generic;
using Shared.Contracts.Chat;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Sessions;

namespace Server.App.Chat
{
    internal sealed class ChatRoomActor : Actor<string>
    {
        internal const int MaxRecentMessages = 100;
        internal readonly Dictionary<GameSessionKey, ChatRoomMember> Members = new();
        internal readonly Queue<ChatMessage> RecentMessages = new();
    }

    internal sealed record ChatRoomMember(string Name);
}
