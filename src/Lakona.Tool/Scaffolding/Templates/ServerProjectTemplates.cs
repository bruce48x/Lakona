internal static class ServerProjectTemplates
{
    public static string RenderServerSolution()
    {
        return """
        <Solution>
          <Project Path="../Shared/Shared.csproj" />
          <Project Path="Hotfix/Server.Hotfix.csproj" />
          <Project Path="App/Server.App.csproj" />
        </Solution>
        """;
    }

    public static string RenderServerProgram(NewCommandOptions options)
    {
        var (serializerPackage, serializerType) = PackageCatalog.GetSerializerArtifacts(options.Serializer);
        var (transportPackage, _) = PackageCatalog.GetTransportArtifacts(options.Transport);
        var transport = TemplateText.SanitizeStringLiteral(options.Transport);
        var acceptorFactory = RenderAcceptorFactory(options);

        return $$"""
        using Microsoft.Extensions.DependencyInjection;
        using Server.App.Chat;
        using Server.App.Hosting;
        using Lakona.Game.Server.Hosting;
        using {{serializerPackage.Namespace}};
        using {{transportPackage.Namespace}};

        return await LakonaGameServer.RunAsync(args, server => server
            .UseTransport("{{transport}}")
            .UseSerializer(() => new {{serializerType}}())
            .UseAcceptor({{acceptorFactory}})
            .AddServices(services => services.AddSingleton<ChatConnectionLifecycle>())
            .BindServices(ServiceBindingConfigurator.Bind));
        """;
    }

    public static string RenderGeneratedServerApplication(NewCommandOptions options)
    {
        if (ProjectConventions.IsRealtimeNetworkProfile(options.NetworkProfile))
        {
            return RenderRealtimeProgram(options);
        }

        return string.Empty;
    }

    private static string RenderRealtimeProgram(NewCommandOptions options)
    {
        var (serializerPackage, serializerType) = PackageCatalog.GetSerializerArtifacts(options.Serializer);
        var (wsTransportPackage, _) = PackageCatalog.GetTransportArtifacts("websocket");
        var (kcpTransportPackage, _) = PackageCatalog.GetTransportArtifacts("kcp");
        var wsAcceptor = RenderAcceptorFactory(CloneOptionsWithTransport(options, "websocket"));
        var kcpAcceptor = RenderAcceptorFactory(CloneOptionsWithTransport(options, "kcp"));

        return $$"""
        using Microsoft.Extensions.DependencyInjection;
        using Server.App.Chat;
        using Server.App.Hosting;
        using Lakona.Game.Server.Hosting;
        using {{serializerPackage.Namespace}};
        using {{wsTransportPackage.Namespace}};
        using {{kcpTransportPackage.Namespace}};

        namespace Server.Hosting.Advanced;

        internal static class LakonaGameGeneratedApplication
        {
            public static async Task<int> RunAsync(string[] args)
            {
                return await LakonaGameServer.RunAsync(args, server => server
                    .UseTransport("websocket")
                    .UseSerializer(() => new {{serializerType}}())
                    .UseAcceptor({{wsAcceptor}})
                    .AddServices(services => services.AddSingleton<ChatConnectionLifecycle>())
                    .BindServices(ServiceBindingConfigurator.Bind)
                    .AddRpcEndpoint("realtime",
                        "kcp",
                        () => new {{serializerType}}(),
                        {{kcpAcceptor}},
                        ServiceBindingConfigurator.Bind));
            }
        }
        """;
    }

    private static NewCommandOptions CloneOptionsWithTransport(NewCommandOptions options, string transport)
    {
        return options with { Transport = transport };
    }

    public static string RenderServerProject(NewCommandOptions options)
    {
        var persistenceReferences = RenderPersistencePackageReferences(options.Persistence, includeDapper: true);
        var clusterReferences = RenderClusterPackageReferences(options);
        var rpcTransportPackage = options.Transport switch
        {
            "tcp" => ("Lakona.Rpc.Transport.Tcp", ToolPackageVersions.LakonaRpcTransportTcp),
            "websocket" => ("Lakona.Rpc.Transport.WebSocket", ToolPackageVersions.LakonaRpcTransportWebSocket),
            _ => ("Lakona.Rpc.Transport.Kcp", ToolPackageVersions.LakonaRpcTransportKcp)
        };
        var serializerReferences = options.Serializer == "json"
            ? $$"""
                <PackageReference Include="Lakona.Rpc.Serializer.Json" Version="{{ToolPackageVersions.LakonaRpcSerializerJson}}" />
            """
            : "";

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
            <PackageReference Include="Microsoft.Extensions.Hosting" Version="{{ToolPackageVersions.MicrosoftExtensionsHosting}}" />
            <PackageReference Include="Lakona.Game.Server" Version="{{ToolPackageVersions.LakonaGameServer}}" />
            <PackageReference Include="Lakona.Game.Server.Generators" Version="{{ToolPackageVersions.LakonaGameServerGenerators}}" PrivateAssets="all" OutputItemType="Analyzer" />
            <PackageReference Include="Lakona.Game.Server.Hotfix" Version="{{ToolPackageVersions.LakonaGameServerHotfix}}" />
            <PackageReference Include="Lakona.Game.Server.Hotfix.Generators" Version="{{ToolPackageVersions.LakonaGameServerHotfixGenerators}}" PrivateAssets="all" OutputItemType="Analyzer" />
        {{clusterReferences}}
        {{persistenceReferences}}
          </ItemGroup>

          <ItemGroup>
            <PackageReference Include="Lakona.Rpc.Server" Version="{{ToolPackageVersions.LakonaRpcServer}}" />
            <PackageReference Include="{{rpcTransportPackage.Item1}}" Version="{{rpcTransportPackage.Item2}}" />
            <PackageReference Include="Lakona.Rpc.Analyzers" Version="{{ToolPackageVersions.LakonaRpcAnalyzers}}" PrivateAssets="all">
              <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
            </PackageReference>
        {{serializerReferences}}
          </ItemGroup>

          <ItemGroup>
            <None Update="appsettings.json">
              <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
            </None>
          </ItemGroup>
        </Project>
        """;
    }

    public static string RenderServerAppSettings(NewCommandOptions options)
    {
        if (ProjectConventions.IsRealtimeNetworkProfile(options.NetworkProfile))
        {
            return """
            {
              "Lakona.Game": {
                "Node": {
                  "Id": "dev-1"
                },
                "Endpoints": [
                  {
                    "Transport": "websocket",
                    "Host": "127.0.0.1",
                    "Port": 20000,
                    "Path": "/ws"
                  },
                  {
                    "Transport": "kcp",
                    "Host": "127.0.0.1",
                    "Port": 20001
                  }
                ]
              }
            }
            """;
        }

        var pathLine = string.Equals(options.Transport, "websocket", StringComparison.OrdinalIgnoreCase)
            ? "," + Environment.NewLine + "          \"Path\": \"/ws\""
            : string.Empty;

        return $$"""
        {
          "Lakona.Game": {
            "Node": {
              "Id": "dev-1"
            },
            "Endpoints": [
              {
                "Transport": "{{TemplateText.SanitizeStringLiteral(options.Transport)}}",
                "Host": "127.0.0.1",
                "Port": 20000{{pathLine}}
              }
            ]
          }
        }
        """;
    }

    public static string RenderHotfixProject()
    {
        return $$"""
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

    public static string RenderServerChatRoomActor()
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
                private readonly Dictionary<string, (string Name, ILoginCallback LoginCallback, IChatCallback ChatCallback)> _members = new();
                private readonly Queue<ChatMessage> _recentMessages = new();

                public ValueTask<LoginReply> LoginAsync(string connectionId, string playerName, ILoginCallback loginCallback)
                {
                    var member = new ChatMember { Name = playerName };
                    _members[connectionId] = (playerName, loginCallback, null!);

                    BroadcastLogin(cb => cb.OnUserJoined(member));

                    return new ValueTask<LoginReply>(new LoginReply
                    {
                        Members = _members.Values.Select(v => new ChatMember { Name = v.Name }).ToList(),
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
                        return ValueTask.CompletedTask;
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

                    BroadcastChat(cb => cb.OnMessageReceived(msg));
                    return ValueTask.CompletedTask;
                }

                public ValueTask LeaveAsync(string connectionId)
                {
                    if (!_members.Remove(connectionId, out var entry))
                    {
                        return ValueTask.CompletedTask;
                    }

                    BroadcastLogin(cb => cb.OnUserLeft(new ChatUserLeft { Name = entry.Name }));
                    return ValueTask.CompletedTask;
                }

                private void BroadcastLogin(Action<ILoginCallback> action)
                {
                    foreach (var entry in _members.Values)
                    {
                        try { action(entry.LoginCallback); } catch { }
                    }
                }

                private void BroadcastChat(Action<IChatCallback> action)
                {
                    foreach (var entry in _members.Values)
                    {
                        try { action(entry.ChatCallback); } catch { }
                    }
                }
            }
        }
        """;
    }

    public static string RenderHotfixLoginService()
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
                    return _actors.AskAsync<ChatRoomActor, LoginReply>(
                        RoomId,
                        (room, ct) => room.LoginAsync(_connectionId, req.PlayerName, _callback));
                }
            }
        }
        """;
    }

    public static string RenderHotfixChatService()
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

    public static string RenderServerAppAssemblyInfo()
    {
        return @"using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo(""Server.Hotfix"")]
";
    }

    public static string RenderServiceBindingConfigurator()
    {
        return @"using System;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Server.App.Chat;
using Server.App.Generated;
using Shared.Contracts.Chat;
using Lakona.Rpc.Server;

namespace Server.App.Hosting;

internal static class ServiceBindingConfigurator
{
    private static readonly Type LoginServiceType = LoadHotfixType(""Server.Hotfix.Login.LoginService"");
    private static readonly Type ChatServiceType = LoadHotfixType(""Server.Hotfix.Chat.ChatService"");

    private static Type LoadHotfixType(string typeName)
    {
        var hotfixDir = System.IO.Path.Combine(AppContext.BaseDirectory, ""hotfix"");
        var hotfixPath = System.IO.Path.Combine(hotfixDir, ""Server.Hotfix.dll"");
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
}";
    }

    public static string RenderServerChatConnectionLifecycle()
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

    private static string RenderAcceptorFactory(NewCommandOptions options)
    {
        return options.Transport switch
        {
            "websocket" => """async opts => await WsConnectionAcceptor.CreateAsync(opts.Port, string.IsNullOrWhiteSpace(opts.Path) ? "/ws" : opts.Path, opts.Host, default)""",
            "tcp" => """async opts => new TcpConnectionAcceptor(opts.Port, opts.Host)""",
            _ => """async opts => new KcpConnectionAcceptor(opts.Port, opts.Host, 100)"""
        };
    }

    private static string RenderPersistencePackageReferences(string persistence, bool includeDapper)
    {
        if (!ProjectConventions.UsesExternalPersistence(persistence))
        {
            return string.Empty;
        }

        var references = new List<string>();
        if (includeDapper)
        {
            references.Add($"""<PackageReference Include="Dapper" Version="{ToolPackageVersions.Dapper}" />""");
        }

        references.Add(string.Equals(persistence, "mysql", StringComparison.OrdinalIgnoreCase)
            ? $"""<PackageReference Include="MySqlConnector" Version="{ToolPackageVersions.MySqlConnector}" />"""
            : $"""<PackageReference Include="Npgsql" Version="{ToolPackageVersions.Npgsql}" />""");

        return TemplateText.IndentBlock(string.Join(Environment.NewLine, references), 3);
    }

    private static string RenderClusterPackageReferences(NewCommandOptions options)
    {
        if (!ProjectConventions.IsClusterNetworkProfile(options.NetworkProfile))
        {
            return string.Empty;
        }

        var references = new[]
        {
            $"""<PackageReference Include="Lakona.Game.Cluster" Version="{ToolPackageVersions.LakonaGameCluster}" />""",
            $"""<PackageReference Include="Lakona.Game.Cluster.Rpc" Version="{ToolPackageVersions.LakonaGameClusterRpc}" />"""
        };

        return TemplateText.IndentBlock(string.Join(Environment.NewLine, references), 3);
    }
}
