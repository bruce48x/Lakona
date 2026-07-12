using Shared.Contracts.Chat;
using Lakona.Game.Server.Sessions;

namespace Server.App.Chat
{
    public static class ChatRoomIds
    {
        public const string Global = "chat-room/global";
    }

    public sealed class ChatRoomLoginRequest
    {
        public GameSessionKey Session { get; set; }

        public string PlayerName { get; set; } = "";

    }

    public sealed class ChatRoomLoginResult
    {
        public LoginReply Reply { get; set; } = new();

        public IReadOnlyList<GameSessionKey> Recipients { get; set; } = Array.Empty<GameSessionKey>();
    }

    public sealed class ChatRoomSendRequest
    {
        public GameSessionKey Session { get; set; }

        public string Text { get; set; } = "";
    }

    public sealed class ChatRoomSendResult
    {
        public ChatMessage Message { get; set; } = new();

        public IReadOnlyList<GameSessionKey> Recipients { get; set; } = Array.Empty<GameSessionKey>();
    }

    public sealed class ChatRoomLeaveRequest
    {
        public GameSessionKey Session { get; set; }
    }

    public sealed class ChatRoomLeaveResult
    {
        public string Name { get; set; } = "";

        public IReadOnlyList<GameSessionKey> Recipients { get; set; } = Array.Empty<GameSessionKey>();
    }
}
