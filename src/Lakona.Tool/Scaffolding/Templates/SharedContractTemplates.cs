internal static class SharedContractTemplates
{
    public static string RenderSharedProjectHotfixItemGroup()
    {
        return $$"""
        <ItemGroup Condition="'$(TargetFramework)' == 'net10.0'">
          <PackageReference Include="Lakona.Game.Server.Hotfix.Abstractions" Version="{{ToolPackageVersions.LakonaGameServerHotfixAbstractions}}" />
          <PackageReference Include="Lakona.Game.Server.Hotfix" Version="{{ToolPackageVersions.LakonaGameServerHotfix}}" />
          <PackageReference Include="Lakona.Game.Server.Hotfix.Generators" Version="{{ToolPackageVersions.LakonaGameServerHotfixGenerators}}" PrivateAssets="all" />
        </ItemGroup>
        """;
    }

    public static string RenderSharedHotfixAssemblyInfo()
    {
        return """
        using System.Runtime.CompilerServices;

        [assembly: InternalsVisibleTo("Server.Hotfix")]
        """;
    }

    public static string RenderSharedRpcContractIds()
    {
        return """
        namespace Shared.Contracts
        {
            public static class RpcContractIds
            {
                public static class Services
                {
                    public const int Chat = 2;
                }

                public static class ChatServiceMethods
                {
                    public const int JoinAsync = 1;
                    public const int SendAsync = 2;
                    public const int LeaveAsync = 3;
                }

                public static class ChatNotifications
                {
                    public const int MessageReceived = 1;
                    public const int UserJoined = 2;
                    public const int UserLeft = 3;
                }
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
                [RpcMethod(RpcContractIds.ChatServiceMethods.JoinAsync)] ValueTask<ChatJoinReply> JoinAsync(ChatJoinRequest req);
                [RpcMethod(RpcContractIds.ChatServiceMethods.SendAsync)] ValueTask SendAsync(ChatSendRequest req);
                [RpcMethod(RpcContractIds.ChatServiceMethods.LeaveAsync)] ValueTask LeaveAsync(ChatLeaveRequest req);
            }

            [RpcNotificationContract(typeof(IChatService))]
            public interface IChatCallback
            {
                [RpcNotification(RpcContractIds.ChatNotifications.MessageReceived)] void OnMessageReceived(ChatMessage msg);
                [RpcNotification(RpcContractIds.ChatNotifications.UserJoined)] void OnUserJoined(ChatMember member);
                [RpcNotification(RpcContractIds.ChatNotifications.UserLeft)] void OnUserLeft(ChatUserLeft evt);
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
            {{memoryPackable}}public partial class ChatJoinRequest
            {
                {{order0}}public string PlayerName { get; set; } = "";
            }

            {{memoryPackable}}public partial class ChatJoinReply
            {
                {{order0}}public List<ChatMember> Members { get; set; } = new();
                {{order1}}public List<ChatMessage> RecentMessages { get; set; } = new();
            }

            {{memoryPackable}}public partial class ChatSendRequest
            {
                {{order0}}public string Text { get; set; } = "";
            }

            {{memoryPackable}}public partial class ChatLeaveRequest
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

    public static string RenderSharedChatRuleState()
    {
        return """
        #if NET
        using Lakona.Game.Server.Hotfix.Abstractions;

        namespace Shared.Contracts.Chat
        {
            [HotfixState]
            public partial class ChatRuleState
            {
            }
        }
        #endif
        """;
    }
}
