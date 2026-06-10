internal static class SharedContractTemplates
{
    public static string RenderSharedRpcContractIds()
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

    public static string RenderSharedLoginProtocols()
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

    public static string RenderSharedChatProtocols()
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

    public static string RenderSharedChatMessages()
    {
        return RenderSharedChatMessages(CliParser.ParseNewOptions([]));
    }

    public static string RenderSharedChatMessages(NewCommandOptions options)
    {
        var memoryPackUsing = string.Equals(options.Serializer, "memorypack", StringComparison.Ordinal)
            ? "using MemoryPack;\n"
            : "";
        var memoryPackable = string.Equals(options.Serializer, "memorypack", StringComparison.Ordinal)
            ? "[MemoryPackable(GenerateType.VersionTolerant)]\n    "
            : "";
        var order0 = string.Equals(options.Serializer, "memorypack", StringComparison.Ordinal) ? "[MemoryPackOrder(0)] " : "";
        var order1 = string.Equals(options.Serializer, "memorypack", StringComparison.Ordinal) ? "[MemoryPackOrder(1)] " : "";
        var order2 = string.Equals(options.Serializer, "memorypack", StringComparison.Ordinal) ? "[MemoryPackOrder(2)] " : "";

        return $$"""
        using System.Collections.Generic;
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
            }

            {{memoryPackable}}public partial class ChatSendRequest
            {
                {{order0}}public string Text { get; set; } = "";
            }

            {{memoryPackable}}public partial class ChatBindRequest
            {
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
        }
        """;
    }

}
