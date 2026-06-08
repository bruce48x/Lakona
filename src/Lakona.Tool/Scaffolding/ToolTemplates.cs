internal static class ToolTemplates
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

        // Single-endpoint: no separate application file needed -- all wiring in Program.cs
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

    public static string RenderClientChatClient()
    {
        return """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Rpc.Generated;
        using Shared.Contracts.Chat;
        using Lakona.Rpc.Client;

        namespace Client.Chat
        {
            public sealed class ChatClient : IChatCallback, IAsyncDisposable
            {
                private readonly RpcClient _rpcClient;
                private IChatService? _chatService;
                private bool _isConnected;

                public event Action<ChatMessage>? OnMessageReceived;
                public event Action<ChatMember>? OnUserJoined;
                public event Action<string>? OnUserLeft;
                public event Action? OnDisconnected;

                public bool IsConnected => _isConnected;

                public ChatClient(RpcClientOptions options)
                {
                    var callbacks = new RpcClient.RpcNotificationBindings();
                    callbacks.Add(this);

                    _rpcClient = new RpcClient(options, callbacks);
                    _rpcClient.Disconnected += _ =>
                    {
                        _isConnected = false;
                        OnDisconnected?.Invoke();
                    };
                }

                public async Task ConnectAsync(CancellationToken cancellationToken = default)
                {
                    await _rpcClient.ConnectAsync(cancellationToken);
                    _chatService = _rpcClient.Api.Shared.Chat;
                    _isConnected = true;
                }

                public async Task<ChatJoinReply> JoinAsync(string playerName)
                {
                    if (_chatService == null) throw new InvalidOperationException("Not connected.");
                    return await _chatService.JoinAsync(new ChatJoinRequest { PlayerName = playerName });
                }

                public async Task SendAsync(string text)
                {
                    if (_chatService == null) throw new InvalidOperationException("Not connected.");
                    await _chatService.SendAsync(new ChatSendRequest { Text = text });
                }

                public async Task LeaveAsync()
                {
                    if (_chatService == null) return;
                    await _chatService.LeaveAsync(new ChatLeaveRequest());
                }

                public async ValueTask DisposeAsync()
                {
                    _isConnected = false;
                    await _rpcClient.DisposeAsync();
                }

                void IChatCallback.OnMessageReceived(ChatMessage msg)
                {
                    OnMessageReceived?.Invoke(msg);
                }

                void IChatCallback.OnUserJoined(ChatMember member)
                {
                    OnUserJoined?.Invoke(member);
                }

                void IChatCallback.OnUserLeft(ChatUserLeft evt)
                {
                    OnUserLeft?.Invoke(evt.Name);
                }
            }
        }
        """;
    }

    public static string RenderGodotChatScene(NewCommandOptions options)
    {
        var defaultPath = string.Equals(options.Transport, "websocket", StringComparison.OrdinalIgnoreCase) ? "/ws" : "";
        var serializerUsing = options.Serializer switch
        {
            "json" => "using Lakona.Rpc.Serializer.Json;",
            _ => "using Lakona.Rpc.Serializer.MemoryPack;"
        };
        var transportUsing = options.Transport switch
        {
            "tcp" => "using Lakona.Rpc.Transport.Tcp;",
            "websocket" => "using Lakona.Rpc.Transport.WebSocket;",
            _ => "using Lakona.Rpc.Transport.Kcp;"
        };
        var serializerConstructor = options.Serializer switch
        {
            "json" => "new JsonRpcSerializer()",
            _ => "new MemoryPackRpcSerializer()"
        };
        var transportConstructor = options.Transport switch
        {
            "tcp" => "new TcpTransport(_serverHost, _serverPort)",
            "websocket" => "new WsTransport($\"ws://{_serverHost}:{_serverPort}{NormalizePath(_serverPath)}\")",
            _ => "new KcpTransport(_serverHost, _serverPort)"
        };

        return $$"""
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Godot;
        using Shared.Contracts.Chat;
        using Lakona.Rpc.Client;
        using Lakona.Rpc.Core;
        {{serializerUsing}}
        {{transportUsing}}

        namespace Client.Chat
        {
            public partial class ChatScene : Control
            {
                [Export] private string _serverHost = "127.0.0.1";
                [Export] private int _serverPort = 20000;
                [Export] private string _serverPath = "{{TemplateText.SanitizeStringLiteral(defaultPath)}}";

                private readonly CancellationTokenSource _cts = new();
                private ChatClient? _client;
                private LineEdit? _nameField;
                private LineEdit? _messageField;
                private Button? _joinButton;
                private Button? _sendButton;
                private RichTextLabel? _messageLog;
                private Label? _onlineCount;
                private bool _isJoining;
                private bool _isSending;

                public override void _Ready()
                {
                    BuildUi();
                    SetJoinBusy(false);
                    SetSendBusy(false);
                    AppendSystemMessage("Enter a name, click Join, then send a message.");
                }

                private void BuildUi()
                {
                    SetAnchorsPreset(LayoutPreset.FullRect);

                    var background = new ColorRect
                    {
                        Name = "Background",
                        Color = new Color(0.10f, 0.10f, 0.12f, 1.0f)
                    };
                    background.SetAnchorsPreset(LayoutPreset.FullRect);
                    AddChild(background);

                    var margin = new MarginContainer { Name = "Layout" };
                    margin.SetAnchorsPreset(LayoutPreset.FullRect);
                    margin.AddThemeConstantOverride("margin_left", 16);
                    margin.AddThemeConstantOverride("margin_top", 16);
                    margin.AddThemeConstantOverride("margin_right", 16);
                    margin.AddThemeConstantOverride("margin_bottom", 16);
                    AddChild(margin);

                    var layout = new VBoxContainer { Name = "ChatLayout" };
                    layout.AddThemeConstantOverride("separation", 10);
                    margin.AddChild(layout);

                    var header = new HBoxContainer { Name = "Header" };
                    header.AddThemeConstantOverride("separation", 12);
                    layout.AddChild(header);

                    var title = new Label { Name = "Title", Text = "Chat Room" };
                    title.AddThemeFontSizeOverride("font_size", 24);
                    title.AddThemeColorOverride("font_color", new Color(0.92f, 0.94f, 0.98f, 1.0f));
                    title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                    header.AddChild(title);

                    _onlineCount = new Label { Name = "OnlineCount", Text = "Online: --" };
                    _onlineCount.AddThemeColorOverride("font_color", new Color(0.55f, 0.85f, 0.62f, 1.0f));
                    header.AddChild(_onlineCount);

                    _messageLog = new RichTextLabel
                    {
                        Name = "MessageLog",
                        BbcodeEnabled = false,
                        ScrollFollowing = true
                    };
                    _messageLog.AddThemeColorOverride("default_color", new Color(0.88f, 0.90f, 0.94f, 1.0f));
                    _messageLog.SizeFlagsVertical = SizeFlags.ExpandFill;
                    layout.AddChild(_messageLog);

                    var footer = new VBoxContainer { Name = "Footer" };
                    footer.AddThemeConstantOverride("separation", 8);
                    layout.AddChild(footer);

                    var joinRow = new HBoxContainer { Name = "JoinRow" };
                    joinRow.AddThemeConstantOverride("separation", 8);
                    footer.AddChild(joinRow);

                    _nameField = new LineEdit { Name = "NameField", PlaceholderText = "Name", MaxLength = 20 };
                    StyleLineEdit(_nameField);
                    _nameField.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                    joinRow.AddChild(_nameField);

                    _joinButton = new Button { Name = "JoinButton", Text = "Join" };
                    StyleButton(_joinButton);
                    _joinButton.Pressed += OnJoinPressed;
                    joinRow.AddChild(_joinButton);

                    var sendRow = new HBoxContainer { Name = "SendRow" };
                    sendRow.AddThemeConstantOverride("separation", 8);
                    footer.AddChild(sendRow);

                    _messageField = new LineEdit { Name = "MessageField", PlaceholderText = "Message", MaxLength = 500 };
                    StyleLineEdit(_messageField);
                    _messageField.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                    _messageField.TextSubmitted += _ => OnSendPressed();
                    sendRow.AddChild(_messageField);

                    _sendButton = new Button { Name = "SendButton", Text = "Send" };
                    StyleButton(_sendButton);
                    _sendButton.Pressed += OnSendPressed;
                    sendRow.AddChild(_sendButton);
                }

                private async void OnJoinPressed()
                {
                    if (_isJoining)
                    {
                        return;
                    }

                    if (_client != null && _client.IsConnected)
                    {
                        AppendSystemMessage("Already connected.");
                        return;
                    }

                    var name = _nameField?.Text.Trim();
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        AppendSystemMessage("Enter a name before joining.");
                        _nameField?.GrabFocus();
                        return;
                    }

                    SetJoinBusy(true);
                    AppendSystemMessage("Connecting...");

                    var client = new ChatClient(CreateRpcClientOptions());
                    client.OnMessageReceived += msg => CallDeferred(nameof(AppendMessageDeferred), msg.SenderName, msg.Text);
                    client.OnUserJoined += member => CallDeferred(nameof(AppendSystemMessageDeferred), $"{member.Name} joined.");
                    client.OnUserLeft += memberName => CallDeferred(nameof(AppendSystemMessageDeferred), $"{memberName} left.");
                    client.OnDisconnected += () => CallDeferred(nameof(AppendSystemMessageDeferred), "Disconnected from server.");

                    try
                    {
                        await client.ConnectAsync(_cts.Token);
                        var reply = await client.JoinAsync(name);
                        _client = client;
                        AppendSystemMessage($"Connected. {reply.Members.Count} online.");
                        SetOnlineCount(reply.Members.Count);

                        foreach (var msg in reply.RecentMessages)
                        {
                            AppendMessageText(msg.SenderName, msg.Text);
                        }
                    }
                    catch (Exception ex)
                    {
                        AppendSystemMessage($"Connection failed: {ex.Message}");
                        await client.DisposeAsync();
                    }
                    finally
                    {
                        SetJoinBusy(false);
                    }
                }

                private async void OnSendPressed()
                {
                    if (_isSending)
                    {
                        return;
                    }

                    if (_client == null || !_client.IsConnected)
                    {
                        AppendSystemMessage("Join the chat before sending.");
                        return;
                    }

                    var text = _messageField?.Text.Trim();
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        return;
                    }

                    SetSendBusy(true);
                    try
                    {
                        await _client.SendAsync(text);
                        if (_messageField != null)
                        {
                            _messageField.Text = string.Empty;
                        }
                    }
                    catch (Exception ex)
                    {
                        AppendSystemMessage($"Send failed: {ex.Message}");
                    }
                    finally
                    {
                        SetSendBusy(false);
                    }
                }

                public void AppendMessageDeferred(string senderName, string text)
                {
                    AppendMessageText(senderName, text);
                }

                public void AppendSystemMessageDeferred(string text)
                {
                    AppendSystemMessage(text);
                }

                private void AppendMessageText(string senderName, string text)
                {
                    AppendLine($"[{senderName}]: {text}");
                }

                private void AppendSystemMessage(string text)
                {
                    AppendLine($"* {text}");
                }

                private void AppendLine(string text)
                {
                    _messageLog?.AppendText(text + System.Environment.NewLine);
                }

                private void SetOnlineCount(int count)
                {
                    if (_onlineCount != null)
                    {
                        _onlineCount.Text = $"Online: {count}";
                    }
                }

                private void SetJoinBusy(bool isBusy)
                {
                    _isJoining = isBusy;
                    if (_joinButton != null)
                    {
                        _joinButton.Disabled = isBusy;
                        _joinButton.Text = isBusy ? "Joining..." : "Join";
                    }
                }

                private void SetSendBusy(bool isBusy)
                {
                    _isSending = isBusy;
                    if (_sendButton != null)
                    {
                        _sendButton.Disabled = isBusy;
                        _sendButton.Text = isBusy ? "Sending..." : "Send";
                    }
                }

                private RpcClientOptions CreateRpcClientOptions()
                {
                    return new RpcClientOptions(
                        {{transportConstructor}},
                        {{serializerConstructor}})
                        .UseSecurity(ConfigureTransportSecurity);
                }

                private static string NormalizePath(string path)
                {
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        return string.Empty;
                    }

                    return path.StartsWith("/", StringComparison.Ordinal) ? path : "/" + path;
                }

                private static void ConfigureTransportSecurity(TransportSecurityConfig security)
                {
                    security.EnableCompression = false;
                    security.CompressionThresholdBytes = 1024;
                    security.EnableEncryption = false;
                    security.EncryptionKeyBase64 = null;
                }

                private static void StyleLineEdit(LineEdit lineEdit)
                {
                    lineEdit.CustomMinimumSize = new Vector2(0, 36);
                    lineEdit.AddThemeColorOverride("font_color", new Color(0.96f, 0.96f, 0.96f, 1.0f));
                    lineEdit.AddThemeColorOverride("font_placeholder_color", new Color(0.58f, 0.62f, 0.70f, 1.0f));
                }

                private static void StyleButton(Button button)
                {
                    button.CustomMinimumSize = new Vector2(96, 36);
                    button.AddThemeColorOverride("font_color", new Color(0.96f, 0.96f, 0.96f, 1.0f));
                    button.AddThemeColorOverride("font_disabled_color", new Color(0.70f, 0.72f, 0.76f, 1.0f));
                }

                public override void _ExitTree()
                {
                    _cts.Cancel();
                    if (_client is not null)
                    {
                        _ = _client.DisposeAsync();
                    }
                    _cts.Dispose();
                }
            }
        }
        """;
    }

    public static string RenderGodotMainScene()
    {
        return """
        [gd_scene load_steps=2 format=3]

        [ext_resource type="Script" path="res://Scripts/Chat/ChatScene.cs" id="1"]

        [node name="ChatScene" type="Control"]
        layout_mode = 3
        anchors_preset = 15
        anchor_right = 1.0
        anchor_bottom = 1.0
        grow_horizontal = 2
        grow_vertical = 2
        script = ExtResource("1")
        """;
    }

    public static string RenderClientChatUI(NewCommandOptions options)
    {
        var defaultPath = string.Equals(options.Transport, "websocket", StringComparison.OrdinalIgnoreCase) ? "/ws" : "";
        var serializerUsing = options.Serializer switch
        {
            "json" => "using Lakona.Rpc.Serializer.Json;",
            _ => "using Lakona.Rpc.Serializer.MemoryPack;"
        };
        var transportUsing = options.Transport switch
        {
            "tcp" => "using Lakona.Rpc.Transport.Tcp;",
            "websocket" => "using Lakona.Rpc.Transport.WebSocket;",
            _ => "using Lakona.Rpc.Transport.Kcp;"
        };
        var serializerConstructor = options.Serializer switch
        {
            "json" => "new JsonRpcSerializer()",
            _ => "new MemoryPackRpcSerializer()"
        };
        var transportConstructor = options.Transport switch
        {
            "tcp" => "new TcpTransport(_serverHost, _serverPort)",
            "websocket" => "new WsTransport($\"ws://{_serverHost}:{_serverPort}{NormalizePath(_serverPath)}\")",
            _ => "new KcpTransport(_serverHost, _serverPort)"
        };

        return $$"""
        using System;
        using System.Collections.Concurrent;
        using System.Threading;
        using System.Threading.Tasks;
        using Shared.Contracts.Chat;
        using Lakona.Rpc.Client;
        using Lakona.Rpc.Core;
        {{serializerUsing}}
        {{transportUsing}}
        using UnityEngine;
        using UnityEngine.UIElements;

        namespace Client.Chat
        {
            [RequireComponent(typeof(UIDocument))]
            public sealed class ChatUI : MonoBehaviour
            {
                [SerializeField] private string _serverHost = "127.0.0.1";
                [SerializeField] private int _serverPort = 20000;
                [SerializeField] private string _serverPath = "{{TemplateText.SanitizeStringLiteral(defaultPath)}}";

                private readonly CancellationTokenSource _cts = new();
                private readonly ConcurrentQueue<Action> _mainThreadActions = new();
                private ChatClient? _client;
                private TextField? _inputField;
                private TextField? _nameField;
                private ScrollView? _messageList;
                private Label? _onlineCount;
                private Button? _sendButton;
                private Button? _joinButton;
                private bool _isJoining;
                private bool _isSending;

                private async void Start()
                {
                    var root = GetComponent<UIDocument>().rootVisualElement;

                    _inputField = root.Q<TextField>("chat-input");
                    _nameField = root.Q<TextField>("name-field");
                    _messageList = root.Q<ScrollView>("message-list");
                    _onlineCount = root.Q<Label>("online-count");
                    _sendButton = root.Q<Button>("send-button");
                    _joinButton = root.Q<Button>("join-button");

                    if (_sendButton != null)
                    {
                        _sendButton.clicked += OnSendClicked;
                    }

                    _inputField?.RegisterCallback<KeyDownEvent>(evt =>
                    {
                        if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                        {
                            OnSendClicked();
                        }
                    });

                    if (_joinButton != null)
                    {
                        _joinButton.clicked += OnJoinClicked;
                    }

                    SetSendBusy(false);
                    SetJoinBusy(false);
                    AppendSystemMessage("Enter a name, click Join, then send a message.");
                }

                private async void OnJoinClicked()
                {
                    if (_isJoining)
                    {
                        return;
                    }

                    if (_client != null && _client.IsConnected)
                    {
                        AppendSystemMessage("Already connected.");
                        return;
                    }

                    var name = _nameField?.value?.Trim();
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        AppendSystemMessage("Enter a name before joining.");
                        _nameField?.Focus();
                        return;
                    }

                    SetJoinBusy(true);
                    AppendSystemMessage("Connecting...");

                    var client = new ChatClient(CreateRpcClientOptions());
                    client.OnMessageReceived += msg => EnqueueMainThread(() => AppendMessage(msg));
                    client.OnUserJoined += member => EnqueueMainThread(() => OnUserJoinedHandler(member));
                    client.OnUserLeft += memberName => EnqueueMainThread(() => OnUserLeftHandler(memberName));
                    client.OnDisconnected += () => EnqueueMainThread(() => AppendSystemMessage("Disconnected from server."));

                    try
                    {
                        await client.ConnectAsync(_cts.Token);
                        var reply = await client.JoinAsync(name);
                        _client = client;
                        AppendSystemMessage($"Connected. {reply.Members.Count} online.");
                        SetOnlineCount(reply.Members.Count);

                        foreach (var msg in reply.RecentMessages)
                        {
                            AppendMessage(msg);
                        }
                    }
                    catch (Exception ex)
                    {
                        AppendSystemMessage($"Connection failed: {ex.Message}");
                        await client.DisposeAsync();
                    }
                    finally
                    {
                        SetJoinBusy(false);
                    }
                }

                private async void OnSendClicked()
                {
                    if (_isSending)
                    {
                        return;
                    }

                    if (_client == null || !_client.IsConnected)
                    {
                        AppendSystemMessage("Join the chat before sending.");
                        return;
                    }

                    var text = _inputField?.value?.Trim();
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        return;
                    }

                    SetSendBusy(true);
                    try
                    {
                        await _client.SendAsync(text);
                        _inputField!.value = "";
                    }
                    catch (Exception ex)
                    {
                        AppendSystemMessage($"Send failed: {ex.Message}");
                    }
                    finally
                    {
                        SetSendBusy(false);
                    }
                }

                private void Update()
                {
                    while (_mainThreadActions.TryDequeue(out var action))
                    {
                        try
                        {
                            action();
                        }
                        catch (Exception ex)
                        {
                            Debug.LogException(ex);
                        }
                    }
                }

                private void EnqueueMainThread(Action action)
                {
                    _mainThreadActions.Enqueue(action);
                }

                private void AppendMessage(ChatMessage msg)
                {
                    var label = new Label($"[{msg.SenderName}]: {msg.Text}");
                    label.AddToClassList("chat-message");
                    _messageList?.Add(label);
                    _messageList?.ScrollTo(label);
                }

                private void AppendSystemMessage(string text)
                {
                    var label = new Label(text);
                    label.AddToClassList("chat-system");
                    _messageList?.Add(label);
                    _messageList?.ScrollTo(label);
                }

                private void SetOnlineCount(int count)
                {
                    if (_onlineCount != null)
                    {
                        _onlineCount.text = $"Online: {count}";
                    }
                }

                private void SetJoinBusy(bool isBusy)
                {
                    _isJoining = isBusy;
                    if (_joinButton != null)
                    {
                        _joinButton.SetEnabled(!isBusy);
                        _joinButton.text = isBusy ? "Joining..." : "Join";
                    }
                }

                private void SetSendBusy(bool isBusy)
                {
                    _isSending = isBusy;
                    if (_sendButton != null)
                    {
                        _sendButton.SetEnabled(!isBusy);
                        _sendButton.text = isBusy ? "Sending..." : "Send";
                    }
                }

                private RpcClientOptions CreateRpcClientOptions()
                {
                    return new RpcClientOptions(
                        {{transportConstructor}},
                        {{serializerConstructor}})
                        .UseSecurity(ConfigureTransportSecurity);
                }

                private static string NormalizePath(string path)
                {
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        return string.Empty;
                    }

                    return path.StartsWith("/", StringComparison.Ordinal) ? path : "/" + path;
                }

                private static void ConfigureTransportSecurity(TransportSecurityConfig security)
                {
                    security.EnableCompression = false;
                    security.CompressionThresholdBytes = 1024;
                    security.EnableEncryption = false;
                    security.EncryptionKeyBase64 = null;
                }

                private void OnUserJoinedHandler(ChatMember member)
                {
                    AppendSystemMessage($"{member.Name} joined.");
                }

                private void OnUserLeftHandler(string memberName)
                {
                    AppendSystemMessage($"{memberName} left.");
                }

                private void OnDestroy()
                {
                    _cts.Cancel();
                    if (_client is not null)
                    {
                        _ = _client.DisposeAsync();
                    }
                    _cts.Dispose();
                }
            }
        }
        """;
    }

    public static string RenderClientChatUxml()
    {
        return """
        <ui:UXML xmlns:ui="UnityEngine.UIElements" xmlns:uie="UnityEditor.UIElements">
            <Style src="ChatScene.uss" />
            <ui:VisualElement class="chat-container" style="width: 100%; height: 100%; flex-grow: 1;">
                <ui:VisualElement class="chat-header">
                    <ui:Label text="Chat Room" class="header-title" />
                    <ui:Label text="Online: --" name="online-count" class="header-count" />
                </ui:VisualElement>
                <ui:ScrollView name="message-list" class="message-list" />
                <ui:VisualElement class="chat-footer">
                    <ui:VisualElement name="join-panel" class="join-panel">
                        <ui:TextField name="name-field" label="Name" max-length="20" class="name-field" />
                        <ui:Button text="Join" name="join-button" class="join-button" />
                    </ui:VisualElement>
                    <ui:TextField name="chat-input" label="Message" max-length="500" class="chat-input" />
                    <ui:Button text="Send" name="send-button" class="send-button" />
                </ui:VisualElement>
            </ui:VisualElement>
        </ui:UXML>
        """;
    }

    public static string RenderClientChatUss()
    {
        return """
        .chat-container {
            width: 100%;
            height: 100%;
            flex-grow: 1;
            background-color: rgb(30, 30, 30);
            color: rgb(230, 230, 230);
        }
        .chat-header {
            flex-direction: row;
            padding: 8px 16px;
            background-color: rgb(40, 40, 40);
            border-bottom-width: 1px;
            border-bottom-color: rgb(60, 60, 60);
        }
        .header-title {
            font-size: 18px;
            color: rgb(200, 200, 200);
        }
        .header-count {
            font-size: 14px;
            color: rgb(120, 180, 120);
            margin-left: auto;
        }
        .message-list {
            flex-grow: 1;
            padding: 8px;
        }
        .unity-label {
            color: rgb(230, 230, 230);
        }
        .chat-message {
            font-size: 14px;
            color: rgb(220, 220, 220);
            margin-bottom: 4px;
        }
        .chat-system {
            font-size: 13px;
            color: rgb(140, 140, 140);
            -unity-font-style: italic;
            margin-bottom: 4px;
        }
        .chat-footer {
            padding: 8px;
            background-color: rgb(40, 40, 40);
            border-top-width: 1px;
            border-top-color: rgb(60, 60, 60);
        }
        .join-panel {
            flex-direction: row;
            margin-bottom: 8px;
        }
        .name-field {
            flex-grow: 1;
            margin-right: 8px;
        }
        .name-field .unity-text-field__label,
        .chat-input .unity-text-field__label {
            color: rgb(210, 210, 210);
        }
        .name-field .unity-text-field__input,
        .chat-input .unity-text-field__input {
            color: rgb(245, 245, 245);
            background-color: rgb(24, 24, 24);
            border-top-color: rgb(80, 80, 80);
            border-right-color: rgb(80, 80, 80);
            border-bottom-color: rgb(80, 80, 80);
            border-left-color: rgb(80, 80, 80);
        }
        .join-button {
            width: 80px;
        }
        .chat-input {
            flex-grow: 1;
        }
        .send-button {
            width: 80px;
        }
        .join-button,
        .send-button {
            color: rgb(245, 245, 245);
            background-color: rgb(54, 94, 160);
            border-top-color: rgb(86, 132, 210);
            border-right-color: rgb(86, 132, 210);
            border-bottom-color: rgb(86, 132, 210);
            border-left-color: rgb(86, 132, 210);
        }
        .join-button:disabled,
        .send-button:disabled {
            color: rgb(190, 190, 190);
            background-color: rgb(66, 66, 66);
            border-top-color: rgb(90, 90, 90);
            border-right-color: rgb(90, 90, 90);
            border-bottom-color: rgb(90, 90, 90);
            border-left-color: rgb(90, 90, 90);
        }
        """;
    }

    public static string RenderUnityMonoScriptMeta(string guid)
    {
        return $$"""
        fileFormatVersion: 2
        guid: {{guid}}
        MonoImporter:
          externalObjects: {}
          serializedVersion: 2
          defaultReferences: []
          executionOrder: 0
          icon: {instanceID: 0}
          userData:
          assetBundleName:
          assetBundleVariant:
        """;
    }

    public static string RenderUnityUxmlMeta(string guid)
    {
        return $$"""
        fileFormatVersion: 2
        guid: {{guid}}
        ScriptedImporter:
          internalIDToNameTable: []
          externalObjects: {}
          serializedVersion: 2
          userData:
          assetBundleName:
          assetBundleVariant:
          script: {fileID: 13804, guid: 0000000000000000e000000000000000, type: 0}
        """;
    }

    public static string RenderUnityUssMeta(string guid)
    {
        return $$"""
        fileFormatVersion: 2
        guid: {{guid}}
        ScriptedImporter:
          internalIDToNameTable: []
          externalObjects: {}
          serializedVersion: 2
          userData:
          assetBundleName:
          assetBundleVariant:
          script: {fileID: 12385, guid: 0000000000000000e000000000000000, type: 0}
          disableValidation: 0
        """;
    }

    public static string RenderUnityTssMeta(string guid)
    {
        return $$"""
        fileFormatVersion: 2
        guid: {{guid}}
        ScriptedImporter:
          internalIDToNameTable: []
          externalObjects: {}
          serializedVersion: 2
          userData:
          assetBundleName:
          assetBundleVariant:
          script: {fileID: 12388, guid: 0000000000000000e000000000000000, type: 0}
          disableValidation: 0
        """;
    }

    public static string RenderUnityNativeAssetMeta(string guid)
    {
        return $$"""
        fileFormatVersion: 2
        guid: {{guid}}
        NativeFormatImporter:
          externalObjects: {}
          mainObjectFileID: 11400000
          userData:
          assetBundleName:
          assetBundleVariant:
        """;
    }

    public static string RenderUnityDefaultRuntimeTheme()
    {
        return """
        @import url("unity-theme://default");
        """;
    }

    public static string RenderUnityPanelSettingsAsset(string defaultRuntimeThemeGuid)
    {
        return $$"""
        %YAML 1.1
        %TAG !u! tag:unity3d.com,2011:
        --- !u!114 &11400000
        MonoBehaviour:
          m_ObjectHideFlags: 0
          m_CorrespondingSourceObject: {fileID: 0}
          m_PrefabInstance: {fileID: 0}
          m_PrefabAsset: {fileID: 0}
          m_GameObject: {fileID: 0}
          m_Enabled: 1
          m_EditorHideFlags: 0
          m_Script: {fileID: 19101, guid: 0000000000000000e000000000000000, type: 0}
          m_Name: LakonaGameChatPanelSettings
          m_EditorClassIdentifier:
          themeUss: {fileID: -4733365628477956816, guid: {{defaultRuntimeThemeGuid}}, type: 3}
          m_TargetTexture: {fileID: 0}
          m_ScaleMode: 1
          m_ReferenceSpritePixelsPerUnit: 100
          m_Scale: 1
          m_ReferenceDpi: 96
          m_FallbackDpi: 96
          m_ReferenceResolution: {x: 1200, y: 800}
          m_ScreenMatchMode: 0
          m_Match: 0
          m_SortingOrder: 0
          m_TargetDisplay: 0
          m_ClearDepthStencil: 1
          m_ClearColor: 0
          m_ColorClearValue: {r: 0, g: 0, b: 0, a: 0}
          m_DynamicAtlasSettings:
            m_MinAtlasSize: 64
            m_MaxAtlasSize: 4096
            m_MaxSubTextureSize: 64
            m_ActiveFilters: 31
          m_AtlasBlitShader: {fileID: 9101, guid: 0000000000000000f000000000000000, type: 0}
          m_RuntimeShader: {fileID: 9100, guid: 0000000000000000f000000000000000, type: 0}
          m_RuntimeWorldShader: {fileID: 9102, guid: 0000000000000000f000000000000000, type: 0}
          textSettings: {fileID: 0}
        """;
    }

    public static string RenderUnityChatSceneObjects(
        long gameObjectId,
        long chatUiComponentId,
        long uiDocumentComponentId,
        long transformId,
        string chatUiScriptGuid,
        string uxmlGuid,
        string panelSettingsGuid,
        string serverPath = "")
    {
        return $$"""
        --- !u!1 &{{gameObjectId}}
        GameObject:
          m_ObjectHideFlags: 0
          m_CorrespondingSourceObject: {fileID: 0}
          m_PrefabInstance: {fileID: 0}
          m_PrefabAsset: {fileID: 0}
          serializedVersion: 6
          m_Component:
          - component: {fileID: {{transformId}}}
          - component: {fileID: {{uiDocumentComponentId}}}
          - component: {fileID: {{chatUiComponentId}}}
          m_Layer: 0
          m_Name: Lakona.Game Chat UI
          m_TagString: Untagged
          m_Icon: {fileID: 0}
          m_NavMeshLayer: 0
          m_StaticEditorFlags: 0
          m_IsActive: 1
        --- !u!114 &{{chatUiComponentId}}
        MonoBehaviour:
          m_ObjectHideFlags: 0
          m_CorrespondingSourceObject: {fileID: 0}
          m_PrefabInstance: {fileID: 0}
          m_PrefabAsset: {fileID: 0}
          m_GameObject: {fileID: {{gameObjectId}}}
          m_Enabled: 1
          m_EditorHideFlags: 0
          m_Script: {fileID: 11500000, guid: {{chatUiScriptGuid}}, type: 3}
          m_Name:
          m_EditorClassIdentifier:
          _serverHost: 127.0.0.1
          _serverPort: 20000
          _serverPath: {{serverPath}}
        --- !u!114 &{{uiDocumentComponentId}}
        MonoBehaviour:
          m_ObjectHideFlags: 0
          m_CorrespondingSourceObject: {fileID: 0}
          m_PrefabInstance: {fileID: 0}
          m_PrefabAsset: {fileID: 0}
          m_GameObject: {fileID: {{gameObjectId}}}
          m_Enabled: 1
          m_EditorHideFlags: 0
          m_Script: {fileID: 19102, guid: 0000000000000000e000000000000000, type: 0}
          m_Name:
          m_EditorClassIdentifier:
          m_PanelSettings: {fileID: 11400000, guid: {{panelSettingsGuid}}, type: 2}
          m_ParentUI: {fileID: 0}
          sourceAsset: {fileID: 9197481963319205126, guid: {{uxmlGuid}}, type: 3}
          m_SortingOrder: 0
        --- !u!4 &{{transformId}}
        Transform:
          m_ObjectHideFlags: 0
          m_CorrespondingSourceObject: {fileID: 0}
          m_PrefabInstance: {fileID: 0}
          m_PrefabAsset: {fileID: 0}
          m_GameObject: {fileID: {{gameObjectId}}}
          serializedVersion: 2
          m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}
          m_LocalPosition: {x: 0, y: 0, z: 0}
          m_LocalScale: {x: 1, y: 1, z: 1}
          m_ConstrainProportionsScale: 0
          m_Children: []
          m_Father: {fileID: 0}
          m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}
        """;
    }

    public static string RenderUnityNuGetPackageImportGuard()
    {
        return """
        #if UNITY_EDITOR
        using System;
        using UnityEditor;

        [InitializeOnLoad]
        internal sealed class LakonaGameNuGetPackageImportGuard : AssetPostprocessor
        {
            static LakonaGameNuGetPackageImportGuard()
            {
                EditorApplication.delayCall += DisableExistingAnalyzerPlugins;
            }

            private static void OnPostprocessAllAssets(
                string[] importedAssets,
                string[] deletedAssets,
                string[] movedAssets,
                string[] movedFromAssetPaths)
            {
                foreach (var assetPath in importedAssets)
                {
                    DisableAnalyzerPlugin(assetPath);
                }

                foreach (var assetPath in movedAssets)
                {
                    DisableAnalyzerPlugin(assetPath);
                }
            }

            private static void DisableExistingAnalyzerPlugins()
            {
                var pluginGuids = AssetDatabase.FindAssets("t:PluginImporter", new[] { "Assets/Packages" });
                foreach (var guid in pluginGuids)
                {
                    var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    DisableAnalyzerPlugin(assetPath);
                }
            }

            private static void DisableAnalyzerPlugin(string assetPath)
            {
                var normalizedPath = assetPath.Replace('\\', '/');
                if (!normalizedPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                    normalizedPath.IndexOf("Assets/Packages/", StringComparison.OrdinalIgnoreCase) < 0 ||
                    normalizedPath.IndexOf("/analyzers/", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return;
                }

                var importer = AssetImporter.GetAtPath(assetPath) as PluginImporter;
                if (importer == null)
                {
                    return;
                }

                if (!importer.GetCompatibleWithAnyPlatform() && !importer.GetCompatibleWithEditor())
                {
                    return;
                }

                importer.SetCompatibleWithAnyPlatform(false);
                importer.SetCompatibleWithEditor(false);
                importer.SaveAndReimport();
            }
        }
        #endif
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
        PingServiceBinder.Bind(
            registry,
            callback => ActivatorUtilities.CreateInstance<PingService>(services, callback));

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


    public static string RenderServerDockerfile()
    {
        return """
        FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
        WORKDIR /src
        COPY . .
        RUN dotnet publish Server/Server/Server.csproj -c Release -o /app

        FROM mcr.microsoft.com/dotnet/runtime:10.0
        WORKDIR /app
        COPY --from=build /app .
        ENTRYPOINT ["dotnet", "Server.dll"]
        """;
    }

    public static string RenderClusterCompose(NewCommandOptions options)
    {
        var endpointPath = string.Equals(options.Transport, "websocket", StringComparison.OrdinalIgnoreCase) ? "/ws" : "";
        var advertisedClientEndpoint = RenderAdvertisedClientEndpoint(options.Transport, "gateway", 20000, endpointPath);
        var healthCommand = "dotnet Server.dll --health-check";

        return $$"""
        services:
          gateway:
            build:
              context: .
              dockerfile: Server/Dockerfile
            environment:
              LakonaGame__Endpoints__0__Transport: "{{TemplateText.SanitizeStringLiteral(options.Transport)}}"
              LakonaGame__Endpoints__0__Host: "0.0.0.0"
              LakonaGame__Endpoints__0__Port: "20000"
              LakonaGame__Endpoints__0__Path: "{{TemplateText.SanitizeStringLiteral(endpointPath)}}"
              Cluster__NodeId: "${ULINKGAME_CLUSTER_NODE_ID:-gateway-1}"
              Cluster__AdvertisedEndpoints__cluster: "${ULINKGAME_CLUSTER_ADVERTISED_ENDPOINTS_CLUSTER:-tcp://gateway:21000}"
              Cluster__AdvertisedEndpoints__client: "${ULINKGAME_CLUSTER_ADVERTISED_ENDPOINTS_CLIENT:-{{TemplateText.SanitizeStringLiteral(advertisedClientEndpoint)}}}"
              Cluster__Bootstrap__NodeDirectoryEndpoints__0: "${ULINKGAME_CLUSTER_BOOTSTRAP_NODE_DIRECTORY_ENDPOINT_0:-tcp://gateway:21000}"
              Cluster__NodeDirectory__Enabled: "${ULINKGAME_CLUSTER_NODE_DIRECTORY_ENABLED:-true}"
              Cluster__NodeDirectory__Storage__Mode: "${ULINKGAME_CLUSTER_NODE_DIRECTORY_STORAGE_MODE:-InMemory}"
              Cluster__Services__0__Kind: "node-directory"
              Cluster__Services__0__Name: "node-directory"
              Cluster__Services__1__Kind: "route-directory"
              Cluster__Services__1__Name: "route-directory"
              Cluster__Services__2__Kind: "gateway"
              Cluster__Services__2__Name: "gateway"
              Cluster__RouteLeaseSeconds: "${ULINKGAME_CLUSTER_ROUTE_LEASE_SECONDS:-30}"
              Cluster__SendTimeoutMilliseconds: "${ULINKGAME_CLUSTER_SEND_TIMEOUT_MILLISECONDS:-2000}"
            ports:
              - "20000:20000"
            healthcheck:
              test: ["CMD-SHELL", "{{TemplateText.SanitizeStringLiteral(healthCommand)}}"]
              interval: 10s
              timeout: 3s
              retries: 3
              start_period: 10s
        """;
    }

    public static string RenderClusterEnvExample(NewCommandOptions options)
    {
        var endpointPath = string.Equals(options.Transport, "websocket", StringComparison.OrdinalIgnoreCase) ? "/ws" : "";
        var advertisedClientEndpoint = RenderAdvertisedClientEndpoint(options.Transport, "gateway", 20000, endpointPath);

        return $$"""
        # This file intentionally contains no production secrets.
        # Put node authentication and TLS material in your deployment platform secret store.
        ULINKGAME_CLUSTER_NODE_ID=gateway-1
        ULINKGAME_CLUSTER_ADVERTISED_ENDPOINTS_CLUSTER=tcp://gateway:21000
        ULINKGAME_CLUSTER_ADVERTISED_ENDPOINTS_CLIENT={{advertisedClientEndpoint}}
        ULINKGAME_CLUSTER_BOOTSTRAP_NODE_DIRECTORY_ENDPOINT_0=tcp://gateway:21000
        ULINKGAME_CLUSTER_NODE_DIRECTORY_ENABLED=true
        ULINKGAME_CLUSTER_NODE_DIRECTORY_STORAGE_MODE=InMemory
        ULINKGAME_CLUSTER_ROUTE_LEASE_SECONDS=30
        ULINKGAME_CLUSTER_SEND_TIMEOUT_MILLISECONDS=2000
        """;
    }

    public static string RenderClusterOperationsGuide()
    {
        return """
        # Cluster Operations

        This scaffold is an opt-in starting point for local cluster deployment rehearsal.

        It intentionally does not define production secrets. Node authentication keys, TLS certificates, database credentials, and deployment tokens must come from the deployment platform secret store or a project-owned secret management flow.

        Generated cluster settings can be overridden with environment variables:

        - `Cluster__NodeId`
        - `Cluster__AdvertisedEndpoints__cluster`
        - `Cluster__AdvertisedEndpoints__client`
        - `Cluster__Bootstrap__NodeDirectoryEndpoints__0`
        - `Cluster__NodeDirectory__Enabled`
        - `Cluster__NodeDirectory__Storage__Mode`
        - `Cluster__Services__0__Kind`
        - `Cluster__Services__0__Name`
        - `Cluster__RouteLeaseSeconds`
        - `Cluster__SendTimeoutMilliseconds`

        Health check:

        ```bash
        dotnet Server.dll --health-check
        ```

        The generated health check validates that local cluster configuration has a node id, at least one advertised endpoint, and at least one configured service. Remote node-directory, route-directory, and node-messenger dependency checks should be wired by the project host using `ULinkRpcClusterDependencyProbe` once the project chooses its concrete topology and secret policy.
        """;
    }


    private static string RenderAdvertisedClientEndpoint(
        string transport,
        string host,
        int port,
        string path)
    {
        var scheme = transport switch
        {
            "websocket" => "ws",
            "tcp" => "tcp",
            _ => "kcp"
        };
        return string.IsNullOrWhiteSpace(path)
            ? $"{scheme}://{host}:{port}"
            : $"{scheme}://{host}:{port}{path}";
    }
}

internal static class PackageCatalog
{
    public static (PackageArtifact PackageId, string SerializerType) GetSerializerArtifacts(string serializer)
    {
        return serializer switch
        {
            "json" => (new PackageArtifact("Lakona.Rpc.Serializer.Json", "", "Lakona.Rpc.Serializer.Json"), "JsonRpcSerializer"),
            _ => (new PackageArtifact("Lakona.Rpc.Serializer.MemoryPack", "", "Lakona.Rpc.Serializer.MemoryPack"), "MemoryPackRpcSerializer")
        };
    }

    public static (PackageArtifact PackageId, string AcceptorType) GetTransportArtifacts(string transport)
    {
        return transport switch
        {
            "tcp" => (new PackageArtifact("Lakona.Rpc.Transport.Tcp", "", "Lakona.Rpc.Transport.Tcp"), "TcpConnectionAcceptor"),
            "websocket" => (new PackageArtifact("Lakona.Rpc.Transport.WebSocket", "", "Lakona.Rpc.Transport.WebSocket"), "WsConnectionAcceptor"),
            _ => (new PackageArtifact("Lakona.Rpc.Transport.Kcp", "", "Lakona.Rpc.Transport.Kcp"), "KcpConnectionAcceptor")
        };
    }
}

internal static class TemplateText
{
    public static string SanitizeStringLiteral(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    public static string SanitizeCSharpIdentifier(string value)
    {
        var sanitized = new string(value.Select(static c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return "Game";
        }

        return char.IsDigit(sanitized[0]) ? "_" + sanitized : sanitized;
    }

    public static string IndentBlock(string block, int level)
    {
        if (string.IsNullOrWhiteSpace(block))
        {
            return string.Empty;
        }

        var indent = new string(' ', level * 4);
        var lines = block.Replace("\r\n", "\n").Split('\n');
        return string.Join(Environment.NewLine, lines.Select(line => string.IsNullOrWhiteSpace(line) ? string.Empty : indent + line));
    }

}
