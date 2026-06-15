using System;
using Server.App.Chat;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Sessions;

namespace Server.App.Lifecycle
{
    internal sealed class ChatPresenceLifecycleHandler : IGameSessionLifecycleHandler
    {
        private static readonly ActorId RoomId = ActorId.From("chat:global");
        private readonly IActorRuntime _actors;

        public ChatPresenceLifecycleHandler(IActorRuntime actors)
        {
            _actors = actors;
        }

        public ValueTask OnConnectionOpenedAsync(GameConnectionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask OnSessionBoundAsync(GameSessionBindingContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask OnSessionDisconnectedAsync(GameSessionBindingContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }

        public async ValueTask OnSessionExpiredAsync(GameSessionBindingContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                await _actors.AskAsync<ChatRoomActor, bool>(
                    RoomId,
                    async (room, ct) =>
                    {
                        await HotfixDispatch.Invoke<ChatRoomActor, ValueTask>(
                            "LeaveAsync",
                            room,
                            [typeof(string)],
                            [context.ConnectionId]);
                        return true;
                    });
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Chat presence cleanup failed: {ex}");
            }
        }

        public ValueTask OnSessionTerminatedAsync(GameSessionTerminationContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }
}
