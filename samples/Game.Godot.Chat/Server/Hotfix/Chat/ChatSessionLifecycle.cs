using Server.App.Chat;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Server.Hotfix.Chat
{
    [HotfixLifecycle(typeof(IGameSessionLifecycle))]
    internal sealed class ChatSessionLifecycle
    {
        private static readonly ActorId RoomId = ActorId.From("chat:global");

        public static ValueTask SessionDisconnectedAsync(HotfixLifecycleCall<GameSessionDisconnectedRequest> call)
        {
            return default;
        }

        public static async ValueTask SessionExpiredAsync(HotfixLifecycleCall<GameSessionExpiredRequest> call)
        {
            var connectionId = call.Request.ConnectionId;
            if (string.IsNullOrWhiteSpace(connectionId))
            {
                return;
            }
            var localActors = call.Actors;

            await localActors.AskAsync<ChatRoomActor, bool>(
                RoomId,
                async (room, ct) =>
                {
                    await room.LeaveAsync(connectionId);
                    return true;
                });
        }
    }
}
