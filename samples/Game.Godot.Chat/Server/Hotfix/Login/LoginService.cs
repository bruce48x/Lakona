using System;
using System.Threading;
using Lakona.Game.Server;
using Server.App.Chat;
using Server.Hotfix.Chat;
using Shared.Contracts.Chat;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Server.Hotfix.Login
{
    [HotfixService(typeof(ILoginService))]
    internal sealed class LoginService
    {
        private readonly ChatRoomActors _rooms;
        private readonly ILakonaGameServer _gameServer;

        public LoginService(ChatRoomActors rooms, ILakonaGameServer gameServer)
        {
            _rooms = rooms;
            _gameServer = gameServer;
        }

        public async ValueTask<LoginReply> LoginAsync(HotfixServiceCall<LoginRequest, ILoginCallback> call)
        {
            var playerName = string.IsNullOrWhiteSpace(call.Request.PlayerName)
                ? "Player"
                : call.Request.PlayerName.Trim();
            var reply = await _rooms
                .Route(ChatRoomIds.Global)
                .CallAsync(
                    ChatRoomBehavior.LoginAsync,
                    new ChatRoomLoginRequest
                    {
                        ConnectionId = call.ConnectionId,
                        PlayerName = playerName,
                        LoginCallback = call.Callback
                    },
                    CancellationToken.None);
            await _gameServer.StartSessionAsync(
                playerName,
                call.ConnectionId,
                call.Callback);
            return reply;
        }
    }
}
