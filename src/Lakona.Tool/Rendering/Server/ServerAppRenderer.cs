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
        builder.AddFile("Server/App/Chat/LoginServiceProxy.cs", RenderLoginServiceProxy(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Server/App/Chat/ChatServiceProxy.cs", RenderChatServiceProxy(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Server/App/Chat/ChatConnectionLifecycle.cs", RenderChatConnectionLifecycle(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Server/App/Hosting/ServiceBindingConfigurator.cs", RenderServiceBindingConfigurator(), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Server/App/Properties/AssemblyInfo.cs", RenderAssemblyInfo(), FileWriteMode.Replace, GeneratedFileKind.Text);
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
        var serializerUsing = RenderSerializerUsing(spec.Serializer);
        var serializerExpression = RenderSerializerExpression(spec.Serializer);
        var transportUsing = RenderTransportUsing(spec.Transport);
        var transportValue = ToolEnumText.ToCliValue(spec.Transport);
        var acceptorExpression = RenderAcceptorExpression(spec.Transport);

        return $$"""
        using System.Threading.Tasks;
        using Microsoft.Extensions.DependencyInjection;
        using Server.App.Chat;
        using Server.App.Hosting;
        using Lakona.Game.Server.Hosting;
        using Lakona.Rpc.Core;
        {{serializerUsing}}
        {{transportUsing}}

        return await LakonaGameServer.RunAsync(args, server => server
            .UseTransport("{{transportValue}}")
            .UseSerializer(() => {{serializerExpression}})
            .UseAcceptor({{acceptorExpression}})
            .AddServices(services => services.AddSingleton<ChatConnectionLifecycle>())
            .BindServices(ServiceBindingConfigurator.Bind));
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

            internal sealed record LoginServiceCall(IActorRuntime Actors, string ConnectionId, ILoginCallback Callback, LoginRequest Request);

            internal sealed record ChatServiceCall(IActorRuntime Actors, string ConnectionId, IChatCallback Callback, ChatBindRequest? BindRequest, ChatSendRequest? SendRequest);
        }
        """;
    }

    private static string RenderLoginServiceProxy()
    {
        return """
        using Shared.Contracts.Chat;
        using Lakona.Game.Server.Actors;
        using Lakona.Game.Server.Hotfix.Abstractions;

        namespace Server.App.Chat
        {
            internal sealed class LoginServiceProxy : ILoginService
            {
                private readonly IHotfixServiceInvoker _hotfix;
                private readonly IActorRuntime _actors;
                private readonly ILoginCallback _callback;
                private readonly string _connectionId;

                public LoginServiceProxy(IHotfixServiceInvoker hotfix, IActorRuntime actors, ILoginCallback callback, string connectionId)
                {
                    _hotfix = hotfix;
                    _actors = actors;
                    _callback = callback;
                    _connectionId = connectionId;
                }

                public ValueTask<LoginReply> LoginAsync(LoginRequest req)
                {
                    return _hotfix.InvokeAsync<ILoginService, LoginServiceCall, LoginReply>(
                        nameof(LoginAsync),
                        new LoginServiceCall(_actors, _connectionId, _callback, req));
                }
            }
        }
        """;
    }

    private static string RenderChatServiceProxy()
    {
        return """
        using Shared.Contracts.Chat;
        using Lakona.Game.Server.Actors;
        using Lakona.Game.Server.Hotfix.Abstractions;

        namespace Server.App.Chat
        {
            internal sealed class ChatServiceProxy : IChatService
            {
                private readonly IHotfixServiceInvoker _hotfix;
                private readonly IActorRuntime _actors;
                private readonly IChatCallback _callback;
                private readonly string _connectionId;

                public ChatServiceProxy(IHotfixServiceInvoker hotfix, IActorRuntime actors, IChatCallback callback, string connectionId)
                {
                    _hotfix = hotfix;
                    _actors = actors;
                    _callback = callback;
                    _connectionId = connectionId;
                }

                public ValueTask BindAsync(ChatBindRequest req)
                {
                    return _hotfix.InvokeAsync<IChatService, ChatServiceCall>(
                        nameof(BindAsync),
                        new ChatServiceCall(_actors, _connectionId, _callback, req, null));
                }

                public ValueTask SendAsync(ChatSendRequest req)
                {
                    return _hotfix.InvokeAsync<IChatService, ChatServiceCall>(
                        nameof(SendAsync),
                        new ChatServiceCall(_actors, _connectionId, _callback, null, req));
                }
            }
        }
        """;
    }

    private static string RenderChatConnectionLifecycle()
    {
        return """
        using System;
        using System.Collections.Concurrent;
        using Lakona.Game.Server.Actors;
        using Lakona.Game.Server.Hotfix.Dispatch;
        using Lakona.Rpc.Server;

        namespace Server.App.Chat
        {
            internal sealed class ChatConnectionLifecycle
            {
                private static readonly ActorId RoomId = ActorId.From("chat:global");
                private readonly ConcurrentDictionary<string, byte> _tracked = new();
                private readonly IActorRuntime _actors;

                public ChatConnectionLifecycle(IActorRuntime actors)
                {
                    _actors = actors;
                }

                public void Track(RpcSession session)
                {
                    if (!_tracked.TryAdd(session.ContextId, 0))
                    {
                        return;
                    }

                    session.Disconnected += ex => { _ = LeaveAsync(session.ContextId); };
                }

                private async Task LeaveAsync(string connectionId)
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
                                    [connectionId]);
                                return true;
                            });
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Chat disconnect cleanup failed: {ex}");
                    }
                    finally
                    {
                        _tracked.TryRemove(connectionId, out _);
                    }
                }
            }
        }
        """;
    }

    private static string RenderServiceBindingConfigurator()
    {
        return """
        using System;
        using Microsoft.Extensions.DependencyInjection;
        using Server.App.Chat;
        using Server.App.Generated;
        using Shared.Contracts.Chat;
        using Lakona.Game.Server.Actors;
        using Lakona.Game.Server.Hotfix.Abstractions;
        using Lakona.Rpc.Server;

        namespace Server.App.Hosting;

        internal static class ServiceBindingConfigurator
        {
            public static void Bind(RpcServiceRegistry registry, IServiceProvider services)
            {
                LoginServiceBinder.BindFactory(
                    registry,
                    session =>
                    {
                        services.GetRequiredService<ChatConnectionLifecycle>().Track(session);
                        return new LoginServiceProxy(
                            services.GetRequiredService<IHotfixServiceInvoker>(),
                            services.GetRequiredService<IActorRuntime>(),
                            new LoginCallbackProxy(session),
                            session.ContextId);
                    });

                ChatServiceBinder.BindFactory(
                    registry,
                    session =>
                    {
                        services.GetRequiredService<ChatConnectionLifecycle>().Track(session);
                        return new ChatServiceProxy(
                            services.GetRequiredService<IHotfixServiceInvoker>(),
                            services.GetRequiredService<IActorRuntime>(),
                            new ChatCallbackProxy(session),
                            session.ContextId);
                    });
            }
        }
        """;
    }

    private static string RenderAssemblyInfo()
    {
        return """
        using System.Runtime.CompilerServices;

        [assembly: InternalsVisibleTo("Server.Hotfix")]
        """;
    }

    private static string RenderSerializerUsing(SerializerKind serializer) => serializer switch
    {
        SerializerKind.Json => "using Lakona.Rpc.Serializer.Json;",
        SerializerKind.MemoryPack => "using Lakona.Rpc.Serializer.MemoryPack;",
        _ => throw new ArgumentOutOfRangeException(nameof(serializer), serializer, null)
    };

    private static string RenderSerializerExpression(SerializerKind serializer) => serializer switch
    {
        SerializerKind.Json => "new JsonRpcSerializer()",
        SerializerKind.MemoryPack => "new MemoryPackRpcSerializer()",
        _ => throw new ArgumentOutOfRangeException(nameof(serializer), serializer, null)
    };

    private static string RenderTransportUsing(TransportKind transport) => transport switch
    {
        TransportKind.Tcp => "using Lakona.Rpc.Transport.Tcp;",
        TransportKind.WebSocket => "using Lakona.Rpc.Transport.WebSocket;",
        TransportKind.Kcp => "using Lakona.Rpc.Transport.Kcp;",
        _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, null)
    };

    private static string RenderAcceptorExpression(TransportKind transport) => transport switch
    {
        TransportKind.Tcp => "opts => Task.FromResult<IRpcConnectionAcceptor>(new TcpConnectionAcceptor(opts.Port, opts.Host))",
        TransportKind.WebSocket => "async opts => await WsConnectionAcceptor.CreateAsync(opts.Port, opts.Path, opts.Host)",
        TransportKind.Kcp => "opts => Task.FromResult<IRpcConnectionAcceptor>(new KcpConnectionAcceptor(opts.Port, opts.Host))",
        _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, null)
    };

    private static string RenderAppSettings(LakonaProjectSpec spec)
    {
        var endpoint = new Dictionary<string, object?>
        {
            ["Transport"] = ToolEnumText.ToCliValue(spec.Transport),
            ["Host"] = "127.0.0.1",
            ["Port"] = 20000
        };
        if (spec.Transport == TransportKind.WebSocket)
        {
            endpoint["Path"] = "/ws";
        }

        var settings = new Dictionary<string, object?>
        {
            ["Lakona.Game"] = new Dictionary<string, object?>
            {
                ["Node"] = new Dictionary<string, object?>
                {
                    ["Id"] = "dev-1"
                },
                ["Endpoints"] = new[] { endpoint }
            }
        };

        return JsonSerializer.Serialize(settings, JsonOptions);
    }
}
