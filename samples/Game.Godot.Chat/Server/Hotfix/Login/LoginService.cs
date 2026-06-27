using System;
using Server.App.Chat;
using Server.Hotfix.Chat;
using Shared.Contracts.Chat;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Server.Hotfix.Login
{
    [HotfixService(typeof(ILoginService))]
    internal sealed class LoginService
    {
        public static async ValueTask<LoginReply> LoginAsync(HotfixServiceCall<LoginRequest, ILoginCallback> call)
        {
            var playerName = string.IsNullOrWhiteSpace(call.Request.PlayerName)
                ? "Player"
                : call.Request.PlayerName.Trim();
            var rooms = call.Services.GetRequiredService<ChatRoomActors>();
            var reply = await rooms
                .Get(ChatRoomIds.Global)
                .LoginAsync(new ChatRoomLoginRequest
                {
                    ConnectionId = call.ConnectionId,
                    PlayerName = playerName,
                    LoginCallback = call.Callback
                });
            await call.GameServer.StartSessionAsync(
                playerName,
                call.ConnectionId,
                call.Callback);
            return reply;
        }
    }
}
