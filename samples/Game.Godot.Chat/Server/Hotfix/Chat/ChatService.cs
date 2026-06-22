using System;
using Server.App.Chat;
using Shared.Contracts.Chat;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Server.Hotfix.Chat
{
    [HotfixService(typeof(IChatService))]
    internal sealed class ChatService
    {
        private static readonly ActorId RoomId = ActorId.From("chat:global");

        public static async ValueTask BindAsync(HotfixServiceCall<ChatBindRequest, IChatCallback> call)
        {
            await call.GameServer.BindCurrentSessionAsync(
                call.ConnectionId,
                call.Callback);
            // call.Actors is node-local. Use typed selectors for actors whose placement may be remote.
            var starterNodeLocalActors = call.Actors;

            await starterNodeLocalActors.AskAsync<ChatRoomActor, bool>(
                RoomId,
                (room, ct) =>
                {
                    room.BindChatCallback(call.ConnectionId, call.Callback);
                    return new ValueTask<bool>(true);
                });
        }

        public static async ValueTask SendAsync(HotfixServiceCall<ChatSendRequest, IChatCallback> call)
        {
            // call.Actors is node-local. Use typed selectors for actors whose placement may be remote.
            var starterNodeLocalActors = call.Actors;

            await starterNodeLocalActors.AskAsync<ChatRoomActor, bool>(
                RoomId,
                (room, ct) =>
                {
                    room.BindChatCallback(call.ConnectionId, call.Callback);
                    return new ValueTask<bool>(true);
                });
            var text = FilterMessage(call.Request.Text ?? "");
            await starterNodeLocalActors.AskAsync<ChatRoomActor, bool>(
                RoomId,
                async (room, ct) =>
                {
                    await room.SendAsync(call.ConnectionId, text);
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
