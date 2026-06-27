using System;
using Server.App.Chat;
using Shared.Contracts.Chat;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Server.Hotfix.Chat
{
    [HotfixService(typeof(IChatService))]
    internal sealed class ChatService
    {
        public static async ValueTask BindAsync(HotfixServiceCall<ChatBindRequest, IChatCallback> call)
        {
            await call.GameServer.BindCurrentSessionAsync(
                call.ConnectionId,
                call.Callback);
            var rooms = call.Services.GetRequiredService<ChatRoomActors>();
            await rooms
                .Get(ChatRoomIds.Global)
                .BindChatAsync(new ChatRoomBindRequest
                {
                    ConnectionId = call.ConnectionId,
                    ChatCallback = call.Callback
                });
        }

        public static async ValueTask SendAsync(HotfixServiceCall<ChatSendRequest, IChatCallback> call)
        {
            var rooms = call.Services.GetRequiredService<ChatRoomActors>();
            await rooms
                .Get(ChatRoomIds.Global)
                .BindChatAsync(new ChatRoomBindRequest
                {
                    ConnectionId = call.ConnectionId,
                    ChatCallback = call.Callback
                });
            var text = FilterMessage(call.Request.Text ?? "");
            await rooms
                .Get(ChatRoomIds.Global)
                .SendAsync(new ChatRoomSendRequest
                {
                    ConnectionId = call.ConnectionId,
                    Text = text
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
