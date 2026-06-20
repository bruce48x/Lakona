using Server.App.Chat;
using Server.App.Hotfix;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Server.Hotfix.Chat
{
    [HotfixService(typeof(IChatRuntimeService))]
    internal sealed class ChatRuntimeService
    {
        private static readonly ActorId RoomId = ActorId.From("chat:global");

        public static async ValueTask SessionExpiredAsync(HotfixServiceCall<ChatSessionExpiredRequest> call)
        {
            var connectionId = call.Request.ConnectionId;
            if (string.IsNullOrWhiteSpace(connectionId))
            {
                return;
            }

            await call.Actors.AskAsync<ChatRoomActor, bool>(
                RoomId,
                async (room, ct) =>
                {
                    await room.LeaveAsync(connectionId);
                    return true;
                });
        }
    }
}
