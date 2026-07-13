using System;
using System.Threading;
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
        private readonly ILogger<ChatService> _logger;
        private readonly ChatNotifier _notifications;

        public ChatService(ChatRoomActors rooms, ILogger<ChatService> logger, ChatNotifier notifications)
        {
            _rooms = rooms;
            _logger = logger;
            _notifications = notifications;
        }

        public ValueTask BindAsync(HotfixServiceCall<ChatBindRequest, IChatCallback> call)
        {
            // The session already owns the connection. Callback proxies are resolved
            // from that connection when notifications are sent.
            return default;
        }

        public async ValueTask SendAsync(HotfixServiceCall<ChatSendRequest, IChatCallback> call)
        {
            var text = call.Request.Text ?? "";
            _logger.LogInformation("Sending {CharacterCount} characters", text.Length);
            var session = call.CurrentSession
                ?? throw new InvalidOperationException("Chat send requires an active Game Session.");
            var result = await _rooms
                .Startup(ChatRoomIds.Global)
                .CallAsync(
                    ChatRoomBehavior.SendAsync,
                    new ChatRoomSendRequest
                    {
                        Session = session,
                        Text = FilterMessage(text)
                    },
                    CancellationToken.None);
            if (result is not null)
            {
                await _notifications.MessageAsync(result.Recipients, result.Message);
            }
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
