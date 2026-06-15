using Lakona.Tool.Domain;
using Lakona.Tool.Planning;

namespace Lakona.Tool.Rendering.Shared;

internal sealed class SharedContractsRenderer : IPlanContributor
{
    public void AddFiles(LakonaProjectSpec spec, GenerationPlanBuilder builder)
    {
        builder.AddFile("Shared/Contracts/RpcContractIds.cs", RenderRpcContractIds(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Shared/Contracts/Login.cs", RenderLoginProtocols(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Shared/Contracts/Chat/ChatProtocols.cs", RenderChatProtocols(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Shared/Contracts/Chat/ChatMessages.cs", RenderChatMessages(spec), FileWriteMode.Replace, GeneratedFileKind.Text);
    }

    private static string RenderRpcContractIds()
    {
        return """
        namespace Shared.Contracts
        {
            public static class RpcContractIds
            {
                public static class Services
                {
                    public const int Login = 1;
                    public const int Chat = 2;
                }

                public static class LoginServiceMethods
                {
                    public const int LoginAsync = 1;
                }

                public static class LoginNotifications
                {
                    public const int UserJoined = 1;
                    public const int UserLeft = 2;
                }

                public static class ChatServiceMethods
                {
                    public const int BindAsync = 1;
                    public const int SendAsync = 2;
                }

                public static class ChatNotifications
                {
                    public const int MessageReceived = 1;
                }
            }
        }
        """;
    }

    private static string RenderLoginProtocols()
    {
        return """
        using System.Threading.Tasks;
        using Shared.Contracts;
        using Lakona.Rpc.Core;

        namespace Shared.Contracts.Chat
        {
            [RpcService(RpcContractIds.Services.Login, NotificationContract = typeof(ILoginCallback))]
            public interface ILoginService
            {
                [RpcMethod(RpcContractIds.LoginServiceMethods.LoginAsync)]
                ValueTask<LoginReply> LoginAsync(LoginRequest req);
            }

            [RpcNotificationContract(typeof(ILoginService))]
            public interface ILoginCallback
            {
                [RpcNotification(RpcContractIds.LoginNotifications.UserJoined)]
                void OnUserJoined(ChatMember member);

                [RpcNotification(RpcContractIds.LoginNotifications.UserLeft)]
                void OnUserLeft(ChatUserLeft evt);
            }
        }
        """;
    }

    private static string RenderChatProtocols()
    {
        return """
        using System.Threading.Tasks;
        using Shared.Contracts;
        using Lakona.Rpc.Core;

        namespace Shared.Contracts.Chat
        {
            [RpcService(RpcContractIds.Services.Chat, NotificationContract = typeof(IChatCallback))]
            public interface IChatService
            {
                [RpcMethod(RpcContractIds.ChatServiceMethods.BindAsync)]
                ValueTask BindAsync(ChatBindRequest req);

                [RpcMethod(RpcContractIds.ChatServiceMethods.SendAsync)]
                ValueTask SendAsync(ChatSendRequest req);
            }

            [RpcNotificationContract(typeof(IChatService))]
            public interface IChatCallback
            {
                [RpcNotification(RpcContractIds.ChatNotifications.MessageReceived)]
                void OnMessageReceived(ChatMessage msg);
            }
        }
        """;
    }

    private static string RenderChatMessages(LakonaProjectSpec spec)
    {
        var memoryPackUsing = spec.Serializer == SerializerKind.MemoryPack ? "using MemoryPack;\n" : "";
        var memoryPackable = spec.Serializer == SerializerKind.MemoryPack ? "[MemoryPackable(GenerateType.VersionTolerant)]\n    " : "";
        var order0 = spec.Serializer == SerializerKind.MemoryPack ? "[MemoryPackOrder(0)] " : "";
        var order1 = spec.Serializer == SerializerKind.MemoryPack ? "[MemoryPackOrder(1)] " : "";
        var order2 = spec.Serializer == SerializerKind.MemoryPack ? "[MemoryPackOrder(2)] " : "";
        var gameSessionKeyFormatterAttribute = spec.Serializer == SerializerKind.MemoryPack
            ? "[MemoryPackAllowSerialize] [GameSessionKeyMemoryPackFormatter] "
            : "";
        var gameSessionKeyFormatter = spec.Serializer == SerializerKind.MemoryPack
            ? """

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
        """
            : "";

        return $$"""
        using System.Collections.Generic;
        using Lakona.Game.Abstractions;
        {{memoryPackUsing}}
        namespace Shared.Contracts.Chat
        {
            {{memoryPackable}}public partial class LoginRequest
            {
                {{order0}}public string PlayerName { get; set; } = "";
            }

            {{memoryPackable}}public partial class LoginReply
            {
                {{order0}}public List<ChatMember> Members { get; set; } = new();
                {{order1}}public List<ChatMessage> RecentMessages { get; set; } = new();
                {{order2}}{{gameSessionKeyFormatterAttribute}}public GameSessionKey Session { get; set; }
            }

            {{memoryPackable}}public partial class ChatSendRequest
            {
                {{order0}}public string Text { get; set; } = "";
            }

            {{memoryPackable}}public partial class ChatBindRequest
            {
                {{order0}}{{gameSessionKeyFormatterAttribute}}public GameSessionKey Session { get; set; }
            }

            {{memoryPackable}}public partial class ChatUserLeft
            {
                {{order0}}public string Name { get; set; } = "";
            }

            {{memoryPackable}}public partial class ChatMember
            {
                {{order0}}public string Name { get; set; } = "";
            }

            {{memoryPackable}}public partial class ChatMessage
            {
                {{order0}}public string SenderName { get; set; } = "";
                {{order1}}public string Text { get; set; } = "";
                {{order2}}public long Timestamp { get; set; }
            }
        {{gameSessionKeyFormatter}}
        }
        """;
    }
}
