using System.Collections.Generic;
using MemoryPack;

namespace Shared.Contracts.Chat
{
    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class LoginRequest
    {
        [MemoryPackOrder(0)] public string PlayerName { get; set; } = "";
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class LoginReply
    {
        [MemoryPackOrder(0)] public List<ChatMember> Members { get; set; } = new();
        [MemoryPackOrder(1)] public List<ChatMessage> RecentMessages { get; set; } = new();
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class ChatSendRequest
    {
        [MemoryPackOrder(0)] public string Text { get; set; } = "";
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class ChatBindRequest
    {
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class ChatUserLeft
    {
        [MemoryPackOrder(0)] public string Name { get; set; } = "";
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class ChatMember
    {
        [MemoryPackOrder(0)] public string Name { get; set; } = "";
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class ChatMessage
    {
        [MemoryPackOrder(0)] public string SenderName { get; set; } = "";
        [MemoryPackOrder(1)] public string Text { get; set; } = "";
        [MemoryPackOrder(2)] public long Timestamp { get; set; }
    }
}
