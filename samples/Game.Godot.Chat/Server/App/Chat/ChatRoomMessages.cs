using Shared.Contracts.Chat;
using Lakona.Game.Server.Sessions;
using MemoryPack;

namespace Server.App.Chat
{
    public static class ChatRoomIds
    {
        public const string Global = "chat-room/global";
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class ChatRoomLoginRequest
    {
        [MemoryPackOrder(0)]
        public GameSessionKey Session { get; set; }

        [MemoryPackOrder(1)]
        public string PlayerName { get; set; } = "";

    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class ChatRoomLoginResult
    {
        [MemoryPackOrder(0)]
        public LoginReply Reply { get; set; } = new();

        [MemoryPackOrder(1)]
        public IReadOnlyList<GameSessionKey> Recipients { get; set; } = Array.Empty<GameSessionKey>();
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class ChatRoomSendRequest
    {
        [MemoryPackOrder(0)]
        public GameSessionKey Session { get; set; }

        [MemoryPackOrder(1)]
        public string Text { get; set; } = "";
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class ChatRoomSendResult
    {
        [MemoryPackOrder(0)]
        public ChatMessage Message { get; set; } = new();

        [MemoryPackOrder(1)]
        public IReadOnlyList<GameSessionKey> Recipients { get; set; } = Array.Empty<GameSessionKey>();
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class ChatRoomLeaveRequest
    {
        [MemoryPackOrder(0)]
        public GameSessionKey Session { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class ChatRoomLeaveResult
    {
        [MemoryPackOrder(0)]
        public string Name { get; set; } = "";

        [MemoryPackOrder(1)]
        public IReadOnlyList<GameSessionKey> Recipients { get; set; } = Array.Empty<GameSessionKey>();
    }
}
