using Lakona.Tool.Domain;
using Lakona.Tool.Planning;

namespace Lakona.Tool.Rendering.Server;

internal sealed class HotfixRenderer : IPlanContributor
{
    public void AddFiles(LakonaProjectSpec spec, GenerationPlanBuilder builder)
    {
        builder.AddFile("Server/Hotfix/Server.Hotfix.csproj", RenderProject(), FileWriteMode.Replace, GeneratedFileKind.Project);
        builder.AddFile("Server/Hotfix/Login/LoginService.cs", RenderLoginService(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Server/Hotfix/Chat/ChatService.cs", RenderChatService(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Server/Hotfix/Chat/ChatRoomBehavior.cs", RenderChatRoomBehavior(), FileWriteMode.Replace, GeneratedFileKind.Text);
    }

    private static string RenderProject()
    {
        return """
        <Project Sdk="Microsoft.NET.Sdk">
          <Import Project="..\App\BuildTag.props" />

          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
            <AssemblyName>Server.Hotfix</AssemblyName>
            <RootNamespace>Server.Hotfix</RootNamespace>
            <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
          </PropertyGroup>

          <ItemGroup>
            <ProjectReference Include="..\..\Shared\Shared.csproj" TargetFramework="net10.0">
              <SetTargetFramework>TargetFramework=net10.0</SetTargetFramework>
            </ProjectReference>
            <ProjectReference Include="..\App\Server.App.csproj" />
          </ItemGroup>

          <Target Name="CopyHotfixOutput" AfterTargets="Build">
            <PropertyGroup>
              <LakonaHotfixOutputDir>$(ProjectDir)..\App\bin\$(Configuration)\$(TargetFramework)\hotfix\</LakonaHotfixOutputDir>
            </PropertyGroup>
            <Copy
              SourceFiles="$(TargetPath)"
              DestinationFolder="$(LakonaHotfixOutputDir)" />
            <Copy
              SourceFiles="$(TargetDir)$(AssemblyName).pdb"
              DestinationFolder="$(LakonaHotfixOutputDir)"
              Condition="Exists('$(TargetDir)$(AssemblyName).pdb')" />
            <Copy
              SourceFiles="$(ProjectDepsFilePath)"
              DestinationFolder="$(LakonaHotfixOutputDir)"
              Condition="Exists('$(ProjectDepsFilePath)')" />
            <WriteLinesToFile
              File="$(LakonaHotfixOutputDir)reload.signal"
              Lines="{ &quot;assembly&quot;: &quot;$(TargetFileName)&quot;, &quot;builtAtUtc&quot;: &quot;$([System.DateTime]::UtcNow.ToString('O'))&quot; }"
              Overwrite="true" />
          </Target>
        </Project>
        """;
    }

    private static string RenderLoginService()
    {
        return """
        using System;
        using Server.App.Chat;
        using Server.Hotfix.Chat;
        using Shared.Contracts.Chat;
        using Lakona.Game.Server.Actors;
        using Lakona.Game.Server.Hotfix.Abstractions;

        namespace Server.Hotfix.Login
        {
            [HotfixService(typeof(ILoginService))]
            internal sealed class LoginService
            {
                private static readonly ActorId RoomId = ActorId.From("chat:global");

                public static ValueTask<LoginReply> LoginAsync(LoginServiceCall call)
                {
                    var playerName = string.IsNullOrWhiteSpace(call.Request.PlayerName)
                        ? "Player"
                        : call.Request.PlayerName.Trim();

                    return call.Actors.AskAsync<ChatRoomActor, LoginReply>(
                        RoomId,
                        (room, ct) => room.LoginAsync(call.ConnectionId, playerName, call.Callback));
                }
            }
        }
        """;
    }

    private static string RenderChatService()
    {
        return """
        using System;
        using Server.App.Chat;
        using Shared.Contracts.Chat;
        using Lakona.Game.Server.Actors;
        using Lakona.Game.Server.Hotfix.Abstractions;

        namespace Server.Hotfix.Chat
        {
            [HotfixService(typeof(IChatService))]
            internal sealed class ChatService
            {
                private static readonly ActorId RoomId = ActorId.From("chat:global");

                public static async ValueTask BindAsync(ChatServiceCall call)
                {
                    await call.Actors.AskAsync<ChatRoomActor, bool>(
                        RoomId,
                        (room, ct) =>
                        {
                            room.BindChatCallback(call.ConnectionId, call.Callback);
                            return new ValueTask<bool>(true);
                        });
                }

                public static async ValueTask SendAsync(ChatServiceCall call)
                {
                    await BindAsync(call);
                    var text = FilterMessage(call.SendRequest?.Text ?? "");
                    await call.Actors.AskAsync<ChatRoomActor, bool>(
                        RoomId,
                        async (room, ct) =>
                        {
                            await room.SendAsync(call.ConnectionId, text);
                            return true;
                        });
                }

                private static string FilterMessage(string text)
                {
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        return "<empty>";
                    }

                    var filtered = text.Length > 500 ? text[..500] : text;
                    filtered = filtered.Replace("badword", "***", StringComparison.OrdinalIgnoreCase);
                    return filtered;
                }
            }
        }
        """;
    }

    private static string RenderChatRoomBehavior()
    {
        return """
        using System;
        using System.Linq;
        using Server.App.Chat;
        using Shared.Contracts.Chat;
        using Lakona.Game.Server.Hotfix.Abstractions;

        namespace Server.Hotfix.Chat
        {
            [HotfixBehaviorOf(typeof(ChatRoomActor))]
            internal static class ChatRoomBehavior
            {
                public static ValueTask<LoginReply> LoginAsync(
                    this ChatRoomActor self,
                    string connectionId,
                    string playerName,
                    ILoginCallback loginCallback)
                {
                    var member = new ChatMember { Name = playerName };
                    self.Members[connectionId] = new ChatRoomMember(playerName, loginCallback, null);

                    BroadcastLogin(self, callback => callback.OnUserJoined(member));

                    return new ValueTask<LoginReply>(new LoginReply
                    {
                        Members = self.Members.Values.Select(value => new ChatMember { Name = value.Name }).ToList(),
                        RecentMessages = self.RecentMessages.ToList()
                    });
                }

                public static void BindChatCallback(this ChatRoomActor self, string connectionId, IChatCallback chatCallback)
                {
                    if (self.Members.TryGetValue(connectionId, out var entry))
                    {
                        self.Members[connectionId] = entry with { ChatCallback = chatCallback };
                    }
                }

                public static ValueTask SendAsync(this ChatRoomActor self, string connectionId, string text)
                {
                    if (!self.Members.TryGetValue(connectionId, out var entry))
                    {
                        return default;
                    }

                    var msg = new ChatMessage
                    {
                        SenderName = entry.Name,
                        Text = text,
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    };

                    self.RecentMessages.Enqueue(msg);
                    while (self.RecentMessages.Count > ChatRoomActor.MaxRecentMessages)
                    {
                        self.RecentMessages.Dequeue();
                    }

                    BroadcastChat(self, callback => callback.OnMessageReceived(msg));
                    return default;
                }

                public static ValueTask LeaveAsync(this ChatRoomActor self, string connectionId)
                {
                    if (!self.Members.Remove(connectionId, out var entry))
                    {
                        return default;
                    }

                    BroadcastLogin(self, callback => callback.OnUserLeft(new ChatUserLeft { Name = entry.Name }));
                    return default;
                }

                private static void BroadcastLogin(ChatRoomActor self, Action<ILoginCallback> action)
                {
                    foreach (var entry in self.Members.Values)
                    {
                        try
                        {
                            action(entry.LoginCallback);
                        }
                        catch
                        {
                        }
                    }
                }

                private static void BroadcastChat(ChatRoomActor self, Action<IChatCallback> action)
                {
                    foreach (var entry in self.Members.Values)
                    {
                        if (entry.ChatCallback is null)
                        {
                            continue;
                        }

                        try
                        {
                            action(entry.ChatCallback);
                        }
                        catch
                        {
                        }
                    }
                }
            }
        }
        """;
    }
}
