using System.Text.Json;
using Lakona.Tool.Domain;
using Lakona.Tool.Planning;
using Lakona.Tool.Rendering.Common;

namespace Lakona.Tool.Rendering.Server;

internal sealed class ServerAppRenderer : IPlanContributor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public void AddFiles(LakonaProjectSpec spec, GenerationPlanBuilder builder)
    {
        builder.AddFile("Server/Server.slnx", RenderSolution(), FileWriteMode.Replace, GeneratedFileKind.Solution);
        builder.AddFile("Server/App/BuildTag.props", RenderBuildTagProps(), FileWriteMode.Replace, GeneratedFileKind.Project);
        builder.AddFile("Server/App/Server.App.csproj", RenderProject(spec), FileWriteMode.Replace, GeneratedFileKind.Project);
        builder.AddFile("Server/App/Program.cs", RenderProgram(spec), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Server/App/appsettings.json", RenderAppSettings(spec), FileWriteMode.Replace, GeneratedFileKind.Json);
        builder.AddFile("Server/App/Chat/ChatRoomActor.cs", RenderChatRoomActor(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Server/App/Lifecycle/ChatPresenceLifecycleHandler.cs", RenderChatPresenceLifecycleHandler(), FileWriteMode.Replace, GeneratedFileKind.Text);
    }

    private static string RenderSolution()
    {
        return """
        <Solution>
          <Project Path="../Shared/Shared.csproj" />
          <Project Path="App/Server.App.csproj" />
          <Project Path="Hotfix/Server.Hotfix.csproj" />
        </Solution>
        """;
    }

    private static string RenderProject(LakonaProjectSpec spec)
    {
        var packageReferences = PackageReferenceRenderer.RenderSdkPackageReferences(
            DependencyPlanner.Create(ProjectTarget.ServerApp, spec).PackageReferences);

        return $$"""
        <Project Sdk="Microsoft.NET.Sdk">
          <Import Project="BuildTag.props" />

          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
            <RootNamespace>Server.App</RootNamespace>
            <AssemblyName>Server.App</AssemblyName>
            <BuildInParallel>false</BuildInParallel>
            <RestoreBuildInParallel>false</RestoreBuildInParallel>
            <LakonaRpcGenerateServer>true</LakonaRpcGenerateServer>
            <LakonaRpcServerGeneratedNamespace>Server.App.Generated</LakonaRpcServerGeneratedNamespace>
          </PropertyGroup>

          <ItemGroup>
            <CompilerVisibleProperty Include="LakonaRpcGenerateServer" />
            <CompilerVisibleProperty Include="LakonaRpcServerGeneratedNamespace" />
          </ItemGroup>

          <ItemGroup>
            <ProjectReference Include="..\..\Shared\Shared.csproj" TargetFramework="net10.0">
              <SetTargetFramework>TargetFramework=net10.0</SetTargetFramework>
            </ProjectReference>
          </ItemGroup>

          <ItemGroup>
        {{packageReferences}}
          </ItemGroup>

          <ItemGroup>
            <None Update="appsettings.json" CopyToOutputDirectory="PreserveNewest" />
          </ItemGroup>

          <ItemGroup>
            <AssemblyAttribute Include="System.Reflection.AssemblyMetadataAttribute">
              <_Parameter1>LakonaHotfixBuildTag</_Parameter1>
              <_Parameter2>$(LakonaHotfixBuildTag)</_Parameter2>
            </AssemblyAttribute>
            <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">
              <_Parameter1>Server.Hotfix</_Parameter1>
            </AssemblyAttribute>
          </ItemGroup>
        </Project>
        """;
    }

    private static string RenderBuildTagProps()
    {
        return """
        <Project>
          <PropertyGroup>
            <LakonaHotfixBuildTag>20260612.001</LakonaHotfixBuildTag>
          </PropertyGroup>
        </Project>
        """;
    }

    private static string RenderProgram(LakonaProjectSpec spec)
    {
        return $$"""
        using System.Threading.Tasks;
        using Microsoft.Extensions.DependencyInjection;
        using Server.App.Generated;
        using Server.App.Lifecycle;
        using Lakona.Game.Server.Features;
        using Lakona.Game.Server.Hosting;
        using Lakona.Game.Server.Sessions;

        return await LakonaGameServer.RunAsync(args, server => server
            .AddServices((services, configuration) =>
            {
                services.AddLakonaGame(configuration);
                services.AddSingleton<IGameSessionLifecycleHandler, ChatPresenceLifecycleHandler>();
            })
            .UseGeneratedHotfixServices());
        """;
    }

    private static string RenderChatRoomActor()
    {
        return """
        using System;
        using System.Collections.Generic;
        using Shared.Contracts.Chat;
        using Lakona.Game.Server.Actors;

        namespace Server.App.Chat
        {
            internal sealed class ChatRoomActor : Actor
            {
                internal const int MaxRecentMessages = 100;
                internal readonly Dictionary<string, ChatRoomMember> Members = new(StringComparer.Ordinal);
                internal readonly Queue<ChatMessage> RecentMessages = new();
            }

            internal sealed record ChatRoomMember(string Name, ILoginCallback LoginCallback, IChatCallback? ChatCallback);
        }
        """;
    }

    private static string RenderChatPresenceLifecycleHandler()
    {
        return """
        using System;
        using Server.App.Chat;
        using Lakona.Game.Server.Actors;
        using Lakona.Game.Server.Hotfix.Dispatch;
        using Lakona.Game.Server.Sessions;

        namespace Server.App.Lifecycle
        {
            internal sealed class ChatPresenceLifecycleHandler : IGameSessionLifecycleHandler
            {
                private static readonly ActorId RoomId = ActorId.From("chat:global");
                private readonly IActorRuntime _actors;

                public ChatPresenceLifecycleHandler(IActorRuntime actors)
                {
                    _actors = actors;
                }

                public ValueTask OnConnectionOpenedAsync(GameConnectionContext context, CancellationToken cancellationToken = default)
                {
                    return default;
                }

                public ValueTask OnSessionBoundAsync(GameSessionBindingContext context, CancellationToken cancellationToken = default)
                {
                    return default;
                }

                public ValueTask OnSessionDisconnectedAsync(GameSessionBindingContext context, CancellationToken cancellationToken = default)
                {
                    return default;
                }

                public async ValueTask OnSessionExpiredAsync(GameSessionBindingContext context, CancellationToken cancellationToken = default)
                {
                    try
                    {
                        await _actors.AskAsync<ChatRoomActor, bool>(
                            RoomId,
                            async (room, ct) =>
                            {
                                await HotfixDispatch.Invoke<ChatRoomActor, ValueTask>(
                                    "LeaveAsync",
                                    room,
                                    [typeof(string)],
                                    [context.ConnectionId]);
                                return true;
                            });
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Chat presence cleanup failed: {ex}");
                    }
                }

                public ValueTask OnSessionTerminatedAsync(GameSessionTerminationContext context, CancellationToken cancellationToken = default)
                {
                    return default;
                }
            }
        }
        """;
    }

    private static string RenderAppSettings(LakonaProjectSpec spec)
    {
        var endpoint = new Dictionary<string, object?>
        {
            ["Transport"] = ToolEnumText.ToCliValue(spec.Transport),
            ["Serializer"] = ToolEnumText.ToCliValue(spec.Serializer),
            ["Host"] = "127.0.0.1",
            ["Port"] = 20000,
            ["RpcServices"] = new[] { "login", "chat" }
        };
        if (spec.Transport == TransportKind.WebSocket)
        {
            endpoint["Path"] = "/ws";
        }

        var settings = new Dictionary<string, object?>
        {
            ["Lakona"] = new Dictionary<string, object?>
            {
                ["Node"] = new Dictionary<string, object?>
                {
                    ["Id"] = "dev-1"
                },
                ["Sessions"] = new Dictionary<string, object?>
                {
                    ["Cleanup"] = new Dictionary<string, object?>
                    {
                        ["DisconnectedRetentionSeconds"] = 30
                    }
                },
                ["Endpoints"] = new[] { endpoint }
            }
        };

        return JsonSerializer.Serialize(settings, JsonOptions);
    }
}
