using System;
using System.Threading;
using Lakona.Game.Server;
using Server.App.Generated;
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
        private readonly ActorAccess _actors;
        private readonly ILakonaGameServer _gameServer;
        private readonly ChatNotifier _notifications;

        public LoginService(ActorAccess actors, ILakonaGameServer gameServer, ChatNotifier notifications)
        {
            _actors = actors;
            _gameServer = gameServer;
            _notifications = notifications;
        }

        public async ValueTask<LoginReply> LoginAsync(LoginServiceCall<LoginRequest> call)
        {
            var playerName = string.IsNullOrWhiteSpace(call.Request.PlayerName)
                ? "Player"
                : call.Request.PlayerName.Trim();
            var session = await _gameServer.StartSessionAsync(
                playerName,
                call.ConnectionId);
            var result = await _actors
                .Startup<ChatRoomActor>(ChatRoomIds.Global)
                .CallAsync(
                    ChatRoomBehavior.LoginAsync,
                    new ChatRoomLoginRequest
                    {
                        Session = session,
                        PlayerName = playerName,
                    },
                    CancellationToken.None);
            await _notifications.UserJoinedAsync(
                result.Recipients,
                new ChatMember { Name = playerName });
            return result.Reply;
        }
    }
}
