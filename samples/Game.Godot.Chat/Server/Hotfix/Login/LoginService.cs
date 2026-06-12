using System;
using Server.App.Chat;
using Shared.Contracts.Chat;
using Lakona.Game.Server.Actors;

namespace Server.Hotfix.Login
{
    internal sealed class LoginService : ILoginService
    {
        private static readonly ActorId RoomId = ActorId.From("chat:global");

        private readonly ILoginCallback _callback;
        private readonly IActorRuntime _actors;
        private readonly string _connectionId;

        public LoginService(ILoginCallback callback, IActorRuntime actors, string connectionId)
        {
            _callback = callback;
            _actors = actors;
            _connectionId = connectionId;
        }

        public ValueTask<LoginReply> LoginAsync(LoginRequest req)
        {
            var playerName = string.IsNullOrWhiteSpace(req.PlayerName)
                ? "Player"
                : req.PlayerName.Trim();

            return _actors.AskAsync<ChatRoomActor, LoginReply>(
                RoomId,
                (room, ct) => room.LoginAsync(_connectionId, playerName, _callback));
        }
    }
}
