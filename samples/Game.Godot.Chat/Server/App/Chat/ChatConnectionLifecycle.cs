using System;
using System.Collections.Concurrent;
using Lakona.Game.Server.Actors;
using Lakona.Rpc.Server;

namespace Server.App.Chat
{
    internal sealed class ChatConnectionLifecycle
    {
        private static readonly ActorId RoomId = ActorId.From("chat:global");
        private readonly ConcurrentDictionary<string, byte> _tracked = new();
        private readonly IActorRuntime _actors;

        public ChatConnectionLifecycle(IActorRuntime actors)
        {
            _actors = actors;
        }

        public void Track(RpcSession session)
        {
            if (!_tracked.TryAdd(session.ContextId, 0))
            {
                return;
            }

            session.Disconnected += ex => { _ = LeaveAsync(session.ContextId); };
        }

        private async Task LeaveAsync(string connectionId)
        {
            try
            {
                await _actors.AskAsync<ChatRoomActor, bool>(
                    RoomId,
                    async (room, ct) =>
                    {
                        await room.LeaveAsync(connectionId);
                        return true;
                    });
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Chat disconnect cleanup failed: {ex}");
            }
            finally
            {
                _tracked.TryRemove(connectionId, out _);
            }
        }
    }
}
