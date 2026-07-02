using System;
using Lakona.Game.Server;
using Server.App.Chat;
using Shared.Contracts.Chat;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Microsoft.Extensions.Logging;

namespace Server.Hotfix.Chat
{
    [HotfixService(typeof(IChatService))]
    internal sealed class ChatService
    {
        private readonly ChatRoomActors _rooms;
        private readonly ILakonaGameServer _gameServer;
        private readonly ILogger<ChatService> _logger;

        public ChatService(ChatRoomActors rooms, ILakonaGameServer gameServer, ILogger<ChatService> logger)
        {
            _rooms = rooms;
            _gameServer = gameServer;
            _logger = logger;
        }

        public async ValueTask BindAsync(HotfixServiceCall<ChatBindRequest, IChatCallback> call)
        {
            await _gameServer.BindCurrentSessionAsync(
                call.ConnectionId,
                call.Callback);
            await BindChatCallbackAsync(call.ConnectionId, call.Callback);
        }

        public async ValueTask SendAsync(HotfixServiceCall<ChatSendRequest, IChatCallback> call)
        {
            var text = call.Request.Text ?? "";
            _logger.LogInformation("Sending {CharacterCount} characters", text.Length);
            await BindChatCallbackAsync(call.ConnectionId, call.Callback);
            await _rooms
                .Get(ChatRoomIds.Global)
                .SendAsync(new ChatRoomSendRequest
                {
                    ConnectionId = call.ConnectionId,
                    Text = FilterMessage(text)
                });
        }

        private async ValueTask BindChatCallbackAsync(string connectionId, IChatCallback callback)
        {
            await _rooms
                .Get(ChatRoomIds.Global)
                .BindChatAsync(new ChatRoomBindRequest
                {
                    ConnectionId = connectionId,
                    ChatCallback = callback
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
