using Server.App.Chat;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Server.Hotfix.Chat
{
    [HotfixLifecycle(typeof(IGameSessionLifecycle))]
    internal sealed class ChatSessionLifecycle
    {
        private readonly ChatRoomActors _rooms;

        public ChatSessionLifecycle(ChatRoomActors rooms)
        {
            _rooms = rooms;
        }

        public ValueTask SessionDisconnectedAsync(HotfixLifecycleCall<GameSessionDisconnectedRequest> call)
        {
            return default;
        }

        public async ValueTask SessionExpiredAsync(HotfixLifecycleCall<GameSessionExpiredRequest> call)
        {
            var connectionId = call.Request.ConnectionId;
            if (string.IsNullOrWhiteSpace(connectionId))
            {
                return;
            }

            await _rooms
                .Get(ChatRoomIds.Global)
                .LeaveAsync(new ChatRoomLeaveRequest
                {
                    ConnectionId = connectionId
                });
        }
    }
}
