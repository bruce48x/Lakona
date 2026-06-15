using System.Collections.Generic;
using Lakona.Game.Abstractions;
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
        [MemoryPackOrder(2)] [MemoryPackAllowSerialize] [GameSessionKeyMemoryPackFormatter] public GameSessionKey Session { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class ChatSendRequest
    {
        [MemoryPackOrder(0)] public string Text { get; set; } = "";
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class ChatBindRequest
    {
        [MemoryPackOrder(0)] [MemoryPackAllowSerialize] [GameSessionKeyMemoryPackFormatter] public GameSessionKey Session { get; set; }
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

    internal sealed class GameSessionKeyMemoryPackFormatterAttribute : MemoryPackCustomFormatterAttribute<GameSessionKeyMemoryPackFormatter, GameSessionKey>
    {
        public override GameSessionKeyMemoryPackFormatter GetFormatter()
        {
            return new GameSessionKeyMemoryPackFormatter();
        }
    }

    internal sealed class GameSessionKeyMemoryPackFormatter : MemoryPackFormatter<GameSessionKey>
    {
        public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref GameSessionKey value)
        {
            writer.WriteObjectHeader(3);
            writer.WriteString(value.OwnerKey);
            writer.WriteString(value.SessionId);
            var generation = value.Generation;
            writer.WriteUnmanaged(in generation);
        }

        public override void Deserialize(ref MemoryPackReader reader, scoped ref GameSessionKey value)
        {
            if (!reader.TryReadObjectHeader(out var count))
            {
                value = default;
                return;
            }

            if (count != 3)
            {
                throw new MemoryPackSerializationException("GameSessionKey requires three fields.");
            }

            var ownerKey = reader.ReadString() ?? "";
            var sessionId = reader.ReadString() ?? "";
            var generation = reader.ReadUnmanaged<long>();
            value = new GameSessionKey(ownerKey, sessionId, generation);
        }
    }
}
