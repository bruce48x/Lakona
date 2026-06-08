internal static class ServerProjectTemplates
{
    public static string RenderServerSolution()
    {
        return """
        <Solution>
          <Project Path="../Shared/Shared.csproj" />
          <Project Path="Hotfix/Server.Hotfix.csproj" />
          <Project Path="Server/Server.csproj" />
        </Solution>
        """;
    }

    public static string RenderServerProgram(NewCommandOptions options)
    {
        var (serializerPackage, serializerType) = PackageCatalog.GetSerializerArtifacts(options.Serializer);
        var (transportPackage, _) = PackageCatalog.GetTransportArtifacts(options.Transport);
        var acceptorFactory = RenderAcceptorFactory(options);

        return $$"""
        using Server.Hosting;
        using Lakona.Game.Server.Hosting;
        using {{serializerPackage.Namespace}};
        using {{transportPackage.Namespace}};

        return await LakonaGameServer.RunAsync(args, server => server
            .UseSerializer(() => new {{serializerType}}())
            .UseAcceptor({{acceptorFactory}})
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
        using Server.Hosting;
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
                    .UseSerializer(() => new {{serializerType}}())
                    .UseAcceptor({{wsAcceptor}})
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

        return $$"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
            <RootNamespace>Server</RootNamespace>
            <BuildInParallel>false</BuildInParallel>
            <RestoreBuildInParallel>false</RestoreBuildInParallel>
            <LakonaRpcGenerateServer>true</LakonaRpcGenerateServer>
            <LakonaRpcServerGeneratedNamespace>Server.Generated</LakonaRpcServerGeneratedNamespace>
          </PropertyGroup>

          <ItemGroup>
            <ProjectReference Include="..\..\Shared\Shared.csproj" TargetFramework="net10.0">
              <SetTargetFramework>TargetFramework=net10.0</SetTargetFramework>
            </ProjectReference>
            <ProjectReference Include="..\Hotfix\Server.Hotfix.csproj" ReferenceOutputAssembly="false" />
          </ItemGroup>

          <ItemGroup>
            <PackageReference Include="Microsoft.Extensions.Hosting" Version="{{ToolPackageVersions.MicrosoftExtensionsHosting}}" />
            <PackageReference Include="Lakona.Game.Server" Version="{{ToolPackageVersions.LakonaGameServer}}" />
            <PackageReference Include="Lakona.Game.Server.Generators" Version="{{ToolPackageVersions.LakonaGameServerGenerators}}" PrivateAssets="all" OutputItemType="Analyzer" />
            <PackageReference Include="Lakona.Game.Server.Hotfix" Version="{{ToolPackageVersions.LakonaGameServerHotfix}}" />
        {{clusterReferences}}
        {{persistenceReferences}}
          </ItemGroup>

          <ItemGroup>
            <None Update="appsettings.json">
              <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
            </None>
          </ItemGroup>

          <Target Name="CopyHotfixOutput" AfterTargets="Build">
            <Copy
              SourceFiles="$(ProjectDir)..\Hotfix\bin\$(Configuration)\$(TargetFramework)\Server.Hotfix.dll"
              DestinationFolder="$(OutDir)hotfix\"
              Condition="Exists('$(ProjectDir)..\Hotfix\bin\$(Configuration)\$(TargetFramework)\Server.Hotfix.dll')" />
          </Target>
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
          </ItemGroup>

          <ItemGroup>
            <PackageReference Include="Lakona.Game.Server.Hotfix.Abstractions" Version="{{ToolPackageVersions.LakonaGameServerHotfixAbstractions}}" />
          </ItemGroup>
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

        namespace Server.Chat
        {
            internal sealed class ChatRoomActor : Actor
            {
                private const int MaxRecentMessages = 100;
                private readonly Dictionary<string, (string Name, IChatCallback Callback)> _members = new();
                private readonly Queue<ChatMessage> _recentMessages = new();
                private readonly ChatRules _rules = new();

                public ValueTask<ChatJoinReply> JoinAsync(string connectionId, string playerName, IChatCallback callback)
                {
                    var member = new ChatMember { Name = playerName };
                    _members[connectionId] = (playerName, callback);

                    Broadcast(cb => cb.OnUserJoined(member), excludeConnectionId: null);

                    return new ValueTask<ChatJoinReply>(new ChatJoinReply
                    {
                        Members = _members.Values.Select(v => new ChatMember { Name = v.Name }).ToList(),
                        RecentMessages = _recentMessages.ToList()
                    });
                }

                public ValueTask SendAsync(string connectionId, string text)
                {
                    if (!_members.TryGetValue(connectionId, out var entry))
                    {
                        return ValueTask.CompletedTask;
                    }

                    var filteredText = _rules.FilterMessage(text);
                    var msg = new ChatMessage
                    {
                        SenderName = entry.Name,
                        Text = filteredText,
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    };

                    _recentMessages.Enqueue(msg);
                    while (_recentMessages.Count > MaxRecentMessages)
                    {
                        _recentMessages.Dequeue();
                    }

                    Broadcast(cb => cb.OnMessageReceived(msg), excludeConnectionId: null);
                    return ValueTask.CompletedTask;
                }

                public ValueTask LeaveAsync(string connectionId)
                {
                    if (!_members.Remove(connectionId, out var entry))
                    {
                        return ValueTask.CompletedTask;
                    }

                    Broadcast(cb => cb.OnUserLeft(new ChatUserLeft { Name = entry.Name }), excludeConnectionId: null);
                    return ValueTask.CompletedTask;
                }

                private void Broadcast(Action<IChatCallback> action, string? excludeConnectionId)
                {
                    foreach (var (connId, (_, callback)) in _members)
                    {
                        if (connId == excludeConnectionId)
                        {
                            continue;
                        }

                        try
                        {
                            action(callback);
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

    public static string RenderServerChatRules()
    {
        return """
        using Shared.Contracts.Chat;
        using Lakona.Game.Server.Hotfix.Dispatch;

        namespace Server.Chat
        {
            internal sealed class ChatRules
            {
                private readonly ChatRuleState _state = new();

                public string FilterMessage(string text)
                {
                    return HotfixDispatch.Invoke<ChatRuleState, string, string>(
                        "FilterMessage",
                        _state,
                        text);
                }
            }
        }
        """;
    }

    public static string RenderServerChatServiceImpl()
    {
        return """
        using System;
        using Shared.Contracts.Chat;
        using Lakona.Game.Server.Actors;

        namespace Server.Chat
        {
            internal sealed class ChatServiceImpl : IChatService
            {
                private static readonly ActorId RoomId = ActorId.From("chat:global");

                private readonly IChatCallback _callback;
                private readonly IActorRuntime _actors;
                private readonly string _connectionId;

                public ChatServiceImpl(IChatCallback callback, IActorRuntime actors)
                {
                    _callback = callback;
                    _actors = actors;
                    _connectionId = Guid.NewGuid().ToString("N");
                }

                public ValueTask<ChatJoinReply> JoinAsync(ChatJoinRequest req)
                {
                    return _actors.AskAsync<ChatRoomActor, ChatJoinReply>(
                        RoomId,
                        (room, ct) => room.JoinAsync(_connectionId, req.PlayerName, _callback));
                }

                public async ValueTask SendAsync(ChatSendRequest req)
                {
                    await _actors.AskAsync<ChatRoomActor, bool>(
                        RoomId,
                        async (room, ct) =>
                        {
                            await room.SendAsync(_connectionId, req.Text);
                            return true;
                        });
                }

                public async ValueTask LeaveAsync(ChatLeaveRequest req)
                {
                    await _actors.AskAsync<ChatRoomActor, bool>(
                        RoomId,
                        async (room, ct) =>
                        {
                            await room.LeaveAsync(_connectionId);
                            return true;
                        });
                }
            }
        }
        """;
    }

    public static string RenderHotfixChatSystem()
    {
        return """
        using Shared.Contracts.Chat;
        using Lakona.Game.Server.Hotfix.Abstractions;

        namespace Server.Hotfix.Chat
        {
            [FriendOf(typeof(ChatRuleState))]
            [HotfixSystemOf(typeof(ChatRuleState))]
            public static class ChatRulesSystem
            {
                public static string FilterMessage(this ChatRuleState self, string text)
                {
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        return "<empty>";
                    }

                    var filtered = text.Length > 500 ? text[..500] : text;
                    filtered = filtered.Replace("badword", "***", StringComparison.OrdinalIgnoreCase);
                    filtered = filtered.Replace("脏话", "**", StringComparison.OrdinalIgnoreCase);
                    return filtered;
                }
            }
        }
        """;
    }

    public static string RenderServiceBindingConfigurator()
    {
        return @"using Microsoft.Extensions.DependencyInjection;
using Server.Chat;
using Server.Services;
using Server.Generated;
using Lakona.Rpc.Server;

namespace Server.Hosting;

internal static class ServiceBindingConfigurator
{
    public static void Bind(RpcServiceRegistry registry, IServiceProvider services)
    {
        PingServiceBinder.BindFactory(
            registry,
            _ => ActivatorUtilities.CreateInstance<PingService>(services));

        ChatServiceBinder.Bind(
            registry,
            callback => ActivatorUtilities.CreateInstance<ChatServiceImpl>(services, callback));
    }
}";
    }

    private static string RenderAcceptorFactory(NewCommandOptions options)
    {
        return options.Transport switch
        {
            "websocket" => """async opts => await WsConnectionAcceptor.CreateAsync(opts.Port, string.IsNullOrWhiteSpace(opts.Path) ? "/ws" : opts.Path, default)""",
            "tcp" => """async opts => new TcpConnectionAcceptor(opts.Port)""",
            _ => """async opts => new KcpConnectionAcceptor(opts.Port, 100)"""
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
