using Lakona.Tool.Domain;
using Lakona.Tool.Planning;
using Lakona.Tool.Rendering.Common;

namespace Lakona.Tool.Rendering.Server;

internal sealed class HotfixRenderer : IPlanContributor
{
    public void AddFiles(LakonaProjectSpec spec, GenerationPlanBuilder builder)
    {
        builder.AddFile("Server/Hotfix/Server.Hotfix.csproj", RenderProject(spec), FileWriteMode.Replace, GeneratedFileKind.Project);
        builder.AddFile("Server/Hotfix/Login/LoginService.cs", RenderLoginService(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Server/Hotfix/Chat/ChatService.cs", RenderChatService(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Server/Hotfix/Chat/ChatSessionLifecycle.cs", RenderChatSessionLifecycle(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Server/Hotfix/Chat/ChatRoomBehavior.cs", RenderChatRoomBehavior(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Server/Hotfix/Features/ChatFeature.cs", RenderChatFeature(), FileWriteMode.Replace, GeneratedFileKind.Text);
    }

    private static string RenderProject(LakonaProjectSpec spec)
    {
        var packageReferences = PackageReferenceRenderer.RenderSdkPackageReferences(
            DependencyPlanner.Create(ProjectTarget.ServerHotfix, spec).PackageReferences);

        return $$"""
        <Project Sdk="Microsoft.NET.Sdk">
          <Import Project="..\App\BuildTag.props" />

          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
            <AssemblyName>Server.Hotfix</AssemblyName>
            <RootNamespace>Server.Hotfix</RootNamespace>
            <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
            <LakonaHotfixGenerateStableRpcServices>false</LakonaHotfixGenerateStableRpcServices>
            <LakonaHotfixGenerateStableActorRefs>false</LakonaHotfixGenerateStableActorRefs>
          </PropertyGroup>

          <ItemGroup>
            <CompilerVisibleProperty Include="LakonaHotfixGenerateStableRpcServices" />
            <CompilerVisibleProperty Include="LakonaHotfixGenerateStableActorRefs" />
          </ItemGroup>

          <ItemGroup>
            <ProjectReference Include="..\..\Shared\Shared.csproj" TargetFramework="net10.0">
              <SetTargetFramework>TargetFramework=net10.0</SetTargetFramework>
            </ProjectReference>
            <ProjectReference Include="..\App\Server.App.csproj" />
          </ItemGroup>

          <ItemGroup>
        {{packageReferences}}
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
        """;
    }

    private static string RenderChatService()
    {
        return """
        using System;
        using Server.App.Chat;
        using Shared.Contracts.Chat;
        using Lakona.Game.Server.Hotfix;
        using Lakona.Game.Server.Hotfix.Abstractions;
        using Microsoft.Extensions.DependencyInjection;

        namespace Server.Hotfix.Chat
        {
            [HotfixService(typeof(IChatService))]
            internal sealed class ChatService
            {
                public static async ValueTask BindAsync(HotfixServiceCall<ChatBindRequest, IChatCallback> call)
                {
                    await call.GameServer.BindCurrentSessionAsync(
                        call.ConnectionId,
                        call.Callback);
                    var rooms = call.Services.GetRequiredService<ChatRoomActors>();
                    await rooms
                        .Get(ChatRoomIds.Global)
                        .BindChatAsync(new ChatRoomBindRequest
                        {
                            ConnectionId = call.ConnectionId,
                            ChatCallback = call.Callback
                        });
                }

                public static async ValueTask SendAsync(HotfixServiceCall<ChatSendRequest, IChatCallback> call)
                {
                    var rooms = call.Services.GetRequiredService<ChatRoomActors>();
                    await rooms
                        .Get(ChatRoomIds.Global)
                        .BindChatAsync(new ChatRoomBindRequest
                        {
                            ConnectionId = call.ConnectionId,
                            ChatCallback = call.Callback
                        });
                    var text = FilterMessage(call.Request.Text ?? "");
                    await rooms
                        .Get(ChatRoomIds.Global)
                        .SendAsync(new ChatRoomSendRequest
                        {
                            ConnectionId = call.ConnectionId,
                            Text = text
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
            internal static partial class ChatRoomBehavior
            {
                public static ValueTask<LoginReply> LoginAsync(
                    this ChatRoomActor self,
                    ChatRoomLoginRequest request,
                    CancellationToken cancellationToken = default)
                {
                    _ = cancellationToken;
                    var member = new ChatMember { Name = request.PlayerName };
                    self.Members[request.ConnectionId] = new ChatRoomMember(request.PlayerName, request.LoginCallback, null);

                    BroadcastLogin(self, callback => callback.OnUserJoined(member));

                    return new ValueTask<LoginReply>(new LoginReply
                    {
                        Members = self.Members.Values.Select(value => new ChatMember { Name = value.Name }).ToList(),
                        RecentMessages = self.RecentMessages.ToList()
                    });
                }

                public static ValueTask BindChatAsync(
                    this ChatRoomActor self,
                    ChatRoomBindRequest request,
                    CancellationToken cancellationToken = default)
                {
                    _ = cancellationToken;
                    if (self.Members.TryGetValue(request.ConnectionId, out var entry))
                    {
                        self.Members[request.ConnectionId] = entry with { ChatCallback = request.ChatCallback };
                    }

                    return default;
                }

                public static ValueTask SendAsync(
                    this ChatRoomActor self,
                    ChatRoomSendRequest request,
                    CancellationToken cancellationToken = default)
                {
                    _ = cancellationToken;
                    if (!self.Members.TryGetValue(request.ConnectionId, out var entry))
                    {
                        return default;
                    }

                    var msg = new ChatMessage
                    {
                        SenderName = entry.Name,
                        Text = request.Text,
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

                public static ValueTask LeaveAsync(
                    this ChatRoomActor self,
                    ChatRoomLeaveRequest request,
                    CancellationToken cancellationToken = default)
                {
                    _ = cancellationToken;
                    if (!self.Members.Remove(request.ConnectionId, out var entry))
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

    private static string RenderChatFeature()
    {
        return """
        using Server.App.Chat;
        using Lakona.Game.Server.Hotfix.Abstractions;

        namespace Server.Hotfix.Features
        {
            [HotfixFeature("chat")]
            public sealed class ChatFeature : HotfixGameFeature
            {
                public override void Configure(HotfixFeatureContext context)
                {
                    context.EnsureLocalActor<ChatRoomActor>(ChatRoomIds.Global);
                }
            }
        }
        """;
    }

    private static string RenderChatSessionLifecycle()
    {
        return """
        using Server.App.Chat;
        using Lakona.Game.Server.Hotfix;
        using Lakona.Game.Server.Hotfix.Abstractions;
        using Microsoft.Extensions.DependencyInjection;

        namespace Server.Hotfix.Chat
        {
            [HotfixLifecycle(typeof(IGameSessionLifecycle))]
            internal sealed class ChatSessionLifecycle
            {
                public static ValueTask SessionDisconnectedAsync(HotfixLifecycleCall<GameSessionDisconnectedRequest> call)
                {
                    return default;
                }

                public static async ValueTask SessionExpiredAsync(HotfixLifecycleCall<GameSessionExpiredRequest> call)
                {
                    var connectionId = call.Request.ConnectionId;
                    if (string.IsNullOrWhiteSpace(connectionId))
                    {
                        return;
                    }
                    var rooms = call.Services.GetRequiredService<ChatRoomActors>();
                    await rooms
                        .Get(ChatRoomIds.Global)
                        .LeaveAsync(new ChatRoomLeaveRequest
                        {
                            ConnectionId = connectionId
                        });
                }
            }
        }
        """;
    }
}
