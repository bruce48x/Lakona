using System;
using Server.App.Chat;
using Shared.Contracts.Chat;
using Lakona.Game.Server.Actors;

namespace Server.Hotfix.Chat
{
    internal sealed class ChatService : IChatService
    {
        private static readonly ActorId RoomId = ActorId.From("chat:global");

        private readonly IChatCallback _callback;
        private readonly IActorRuntime _actors;
        private readonly string _connectionId;

        public ChatService(IChatCallback callback, IActorRuntime actors, string connectionId)
        {
            _callback = callback;
            _actors = actors;
            _connectionId = connectionId;
        }

        public async ValueTask BindAsync(ChatBindRequest req)
        {
            await _actors.AskAsync<ChatRoomActor, bool>(
                RoomId,
                (room, ct) =>
                {
                    room.BindChatCallback(_connectionId, _callback);
                    return new ValueTask<bool>(true);
                });
        }

        public async ValueTask SendAsync(ChatSendRequest req)
        {
            await BindAsync(new ChatBindRequest());
            var text = FilterMessage(req.Text);
            await _actors.AskAsync<ChatRoomActor, bool>(
                RoomId,
                async (room, ct) =>
                {
                    await room.SendAsync(_connectionId, text);
                    return true;
                });
        }

        private static string FilterMessage(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "<empty>";
            }

            var filtered = text.Length > 500 ? text[..500] : text;
            filtered = filtered.Replace("badword", "***", StringComparison.OrdinalIgnoreCase);
            return filtered;
        }
    }
}
