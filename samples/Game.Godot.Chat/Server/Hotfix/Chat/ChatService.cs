using System;
using System.Threading;
using Lakona.Game.Server;
using Server.App.Generated;
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
        private readonly ActorAccess _actors;
        private readonly ILogger<ChatService> _logger;
        private readonly ChatNotifier _notifications;

        public ChatService(ActorAccess actors, ILogger<ChatService> logger, ChatNotifier notifications)
        {
            _actors = actors;
            _logger = logger;
            _notifications = notifications;
        }

        public ValueTask BindAsync(ChatServiceCall<ChatBindRequest> call)
        {
            // The session already owns the connection. Callback proxies are resolved
            // from that connection when notifications are sent.
            return default;
        }

        public async ValueTask SendAsync(ChatServiceCall<ChatSendRequest> call)
        {
            var text = call.Request.Text ?? "";
            _logger.LogInformation("Sending {CharacterCount} characters", text.Length);
            var session = call.CurrentSession
                ?? throw new InvalidOperationException("Chat send requires an active Game Session.");
            var result = await _actors
                .Startup<ChatRoomActor>(ChatRoomIds.Global)
                .CallAsync(
                    static behavior => behavior.SendAsync,
                    new ChatRoomSendRequest
                    {
                        Session = session,
                        Text = FilterMessage(text)
                    },
                    CancellationToken.None);
            if (result is not null)
            {
                _notifications.Message(result.Recipients, result.Message);
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
