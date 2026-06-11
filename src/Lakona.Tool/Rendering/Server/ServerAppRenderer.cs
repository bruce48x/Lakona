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
        builder.AddFile("Server/App/Server.App.csproj", RenderProject(spec), FileWriteMode.Replace, GeneratedFileKind.Project);
        builder.AddFile("Server/App/Program.cs", RenderProgram(spec), FileWriteMode.Replace, GeneratedFileKind.Text);
        builder.AddFile("Server/App/appsettings.json", RenderAppSettings(spec), FileWriteMode.Replace, GeneratedFileKind.Json);
        builder.AddFile("Server/App/Chat/ChatRoomActor.cs", RenderChatRoomActor(), FileWriteMode.Replace, GeneratedFileKind.Text);
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
        using System.Linq;
        using Shared.Contracts.Chat;
        using Lakona.Game.Server.Actors;

        namespace Server.App.Chat
        {
            internal sealed class ChatRoomActor : Actor
            {
                private const int MaxRecentMessages = 100;
                private readonly Dictionary<string, (string Name, ILoginCallback LoginCallback, IChatCallback? ChatCallback)> _members = new();
                private readonly Queue<ChatMessage> _recentMessages = new();

                public ValueTask<LoginReply> LoginAsync(string connectionId, string playerName, ILoginCallback loginCallback)
                {
                    var member = new ChatMember { Name = playerName };
                    _members[connectionId] = (playerName, loginCallback, null);

                    BroadcastLogin(callback => callback.OnUserJoined(member));

                    return new ValueTask<LoginReply>(new LoginReply
                    {
                        Members = _members.Values.Select(value => new ChatMember { Name = value.Name }).ToList(),
                        RecentMessages = _recentMessages.ToList()
                    });
                }

                public void BindChatCallback(string connectionId, IChatCallback chatCallback)
                {
                    if (_members.TryGetValue(connectionId, out var entry))
                    {
                        _members[connectionId] = (entry.Name, entry.LoginCallback, chatCallback);
                    }
                }

                public ValueTask SendAsync(string connectionId, string text)
                {
                    if (!_members.TryGetValue(connectionId, out var entry))
                    {
                        return default;
                    }

                    var msg = new ChatMessage
                    {
                        SenderName = entry.Name,
                        Text = text,
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    };

                    _recentMessages.Enqueue(msg);
                    while (_recentMessages.Count > MaxRecentMessages)
                    {
                        _recentMessages.Dequeue();
                    }

                    BroadcastChat(callback => callback.OnMessageReceived(msg));
                    return default;
                }

                public ValueTask LeaveAsync(string connectionId)
                {
                    if (!_members.Remove(connectionId, out var entry))
                    {
                        return default;
                    }

                    BroadcastLogin(callback => callback.OnUserLeft(new ChatUserLeft { Name = entry.Name }));
                    return default;
                }

                private void BroadcastLogin(Action<ILoginCallback> action)
                {
                    foreach (var entry in _members.Values)
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

                private void BroadcastChat(Action<IChatCallback> action)
                {
                    foreach (var entry in _members.Values)
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

    private static string RenderChatConnectionLifecycle()
    {
        return """
        using System;
        using System.Collections.Concurrent;
        using Lakona.Game.Server.Actors;
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
                                await room.LeaveAsync(connectionId);
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
        using System.Reflection;
        using Microsoft.Extensions.DependencyInjection;
        using Server.App.Chat;
        using Server.App.Generated;
        using Shared.Contracts.Chat;
        using Lakona.Rpc.Server;

        namespace Server.App.Hosting;

        internal static class ServiceBindingConfigurator
        {
            private static readonly Type LoginServiceType = LoadHotfixType("Server.Hotfix.Login.LoginService");
            private static readonly Type ChatServiceType = LoadHotfixType("Server.Hotfix.Chat.ChatService");

            private static Type LoadHotfixType(string typeName)
            {
                var hotfixDir = System.IO.Path.Combine(AppContext.BaseDirectory, "hotfix");
                var hotfixPath = System.IO.Path.Combine(hotfixDir, "Server.Hotfix.dll");
                var assembly = Assembly.LoadFrom(hotfixPath);
                return assembly.GetType(typeName, throwOnError: true)!;
            }

            public static void Bind(RpcServiceRegistry registry, IServiceProvider services)
            {
                LoginServiceBinder.BindFactory(
                    registry,
                    session =>
                    {
                        services.GetRequiredService<ChatConnectionLifecycle>().Track(session);
                        return (ILoginService)ActivatorUtilities.CreateInstance(
                            services,
                            LoginServiceType,
                            new LoginCallbackProxy(session),
                            session.ContextId);
                    });

                ChatServiceBinder.BindFactory(
                    registry,
                    session =>
                    {
                        services.GetRequiredService<ChatConnectionLifecycle>().Track(session);
                        return (IChatService)ActivatorUtilities.CreateInstance(
                            services,
                            ChatServiceType,
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
