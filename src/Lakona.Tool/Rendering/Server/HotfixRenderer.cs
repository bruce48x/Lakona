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
    }

    private static string RenderProject()
    {
        return """
        <Project Sdk="Microsoft.NET.Sdk">
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
            <Copy
              SourceFiles="$(TargetPath)"
              DestinationFolder="$(ProjectDir)..\App\bin\$(Configuration)\$(TargetFramework)\hotfix\" />
          </Target>
        </Project>
        """;
    }

    private static string RenderLoginService()
    {
        return """
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
        """;
    }

    private static string RenderChatService()
    {
        return """
        using System;
        using Server.App.Chat;
        using Shared.Contracts.Chat;
        using Lakona.Game.Server.Actors;

        namespace Server.Hotfix.Chat
        {
            internal sealed class ChatService : IChatService
            {
                private static readonly ActorId RoomId = ActorId.From("chat:global");

                private readonly IChatCallback _callback;
                private readonly IActorRuntime _actors;
                private readonly string _connectionId;

                public ChatService(IChatCallback callback, IActorRuntime actors, string connectionId)
                {
                    _callback = callback;
                    _actors = actors;
                    _connectionId = connectionId;
                }

                public async ValueTask BindAsync(ChatBindRequest req)
                {
                    await _actors.AskAsync<ChatRoomActor, bool>(
                        RoomId,
                        (room, ct) =>
                        {
                            room.BindChatCallback(_connectionId, _callback);
                            return new ValueTask<bool>(true);
                        });
                }

                public async ValueTask SendAsync(ChatSendRequest req)
                {
                    await BindAsync(new ChatBindRequest());
                    var text = FilterMessage(req.Text);
                    await _actors.AskAsync<ChatRoomActor, bool>(
                        RoomId,
                        async (room, ct) =>
                        {
                            await room.SendAsync(_connectionId, text);
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
}
