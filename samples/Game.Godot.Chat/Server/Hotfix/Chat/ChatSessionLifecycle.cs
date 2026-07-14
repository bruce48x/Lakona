using System.Threading;
using Server.App.Chat;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Server.Hotfix.Chat
{
    [HotfixLifecycle(typeof(IGameSessionLifecycle))]
    internal sealed class ChatSessionLifecycle
    {
        private readonly ActorAccess _actors;
        private readonly ChatNotifier _notifications;

        public ChatSessionLifecycle(ActorAccess actors, ChatNotifier notifications)
        {
            _actors = actors;
            _notifications = notifications;
        }

        public ValueTask SessionDisconnectedAsync(HotfixLifecycleCall<GameSessionDisconnectedRequest> call)
        {
            // Disconnected sessions stay in the room during the retention window so a client can reconnect without flickering presence.
            return default;
        }

        public async ValueTask SessionExpiredAsync(HotfixLifecycleCall<GameSessionExpiredRequest> call)
        {
            var result = await _actors
                .Startup<ChatRoomActor>(ChatRoomIds.Global)
                .CallAsync(
                    ChatRoomBehavior.LeaveAsync,
                    new ChatRoomLeaveRequest
                    {
                        Session = new Lakona.Game.Server.Sessions.GameSessionKey(
                            call.Request.OwnerKey,
                            call.Request.SessionId,
                            call.Request.Generation)
                    },
                    CancellationToken.None);
            if (result is not null)
            {
                await _notifications.UserLeftAsync(result.Recipients, result.Name);
            }
        }
    }
}
