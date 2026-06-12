using System;
using Server.App.Chat;
using Server.Hotfix.Chat;
using Shared.Contracts.Chat;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Server.Hotfix.Login
{
    [HotfixService(typeof(ILoginService))]
    internal sealed class LoginService
    {
        private static readonly ActorId RoomId = ActorId.From("chat:global");

        public static ValueTask<LoginReply> LoginAsync(LoginServiceCall call)
        {
            var playerName = string.IsNullOrWhiteSpace(call.Request.PlayerName)
                ? "Player"
                : call.Request.PlayerName.Trim();

            return call.Actors.AskAsync<ChatRoomActor, LoginReply>(
                RoomId,
                (room, ct) => room.LoginAsync(call.ConnectionId, playerName, call.Callback));
        }
    }
}
