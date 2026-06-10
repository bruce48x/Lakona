internal static class ChatClientTemplates
{
    public static string RenderClientLoginClient()
    {
        return """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Rpc.Generated;
        using Shared.Contracts.Chat;
        using Lakona.Rpc.Client;

        namespace Client.Login
        {
            public sealed class LoginClient : ILoginCallback, IChatCallback, IAsyncDisposable
            {
                private readonly RpcClient _rpcClient;
                private ILoginService? _loginService;
                private bool _isConnected;

                public event Action<ChatMember>? OnUserJoined;
                public event Action<string>? OnUserLeft;
                public event Action? OnDisconnected;
                public event Action<ChatMessage>? OnMessageReceived;

                public bool IsConnected => _isConnected;
                public RpcClient RpcClient => _rpcClient;

                public LoginClient(RpcClientOptions options)
                {
                    var callbacks = new RpcClient.RpcNotificationBindings();
                    callbacks.Add((ILoginCallback)this);
                    callbacks.Add((IChatCallback)this);

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
                    _loginService = _rpcClient.Api.Shared.Login;
                    _isConnected = true;
                }

                public async Task<LoginReply> LoginAsync(string playerName)
                {
                    if (_loginService == null) throw new InvalidOperationException("Not connected.");
                    return await _loginService.LoginAsync(new LoginRequest { PlayerName = playerName });
                }

                public async ValueTask DisposeAsync()
                {
                    _isConnected = false;
                    await _rpcClient.DisposeAsync();
                }

                void ILoginCallback.OnUserJoined(ChatMember member)
                {
                    OnUserJoined?.Invoke(member);
                }

                void ILoginCallback.OnUserLeft(ChatUserLeft evt)
                {
                    OnUserLeft?.Invoke(evt.Name);
                }

                void IChatCallback.OnMessageReceived(ChatMessage msg)
                {
                    OnMessageReceived?.Invoke(msg);
                }
            }
        }
        """;
    }

    public static string RenderClientChatClient()
    {
        return """
        using System;
        using System.Threading.Tasks;
        using Shared.Contracts.Chat;
        using Client.Login;

        namespace Client.Chat
        {
            public sealed class ChatClient
            {
                private readonly LoginClient _loginClient;
                private readonly IChatService _chatService;

                public event Action<ChatMessage>? OnMessageReceived
                {
                    add { _loginClient.OnMessageReceived += value; }
                    remove { _loginClient.OnMessageReceived -= value; }
                }

                public ChatClient(LoginClient loginClient)
                {
                    _loginClient = loginClient ?? throw new ArgumentNullException(nameof(loginClient));
                    _chatService = loginClient.RpcClient.Api.Shared.Chat;
                }

                public async Task BindAsync()
                {
                    await _chatService.BindAsync(new ChatBindRequest());
                }

                public async Task SendAsync(string text)
                {
                    await _chatService.SendAsync(new ChatSendRequest { Text = text });
                }
            }
        }
        """;
    }

    public static string RenderChatSession()
    {
        return """
        using Shared.Contracts.Chat;
        using Client.Login;

        namespace Client.Chat
        {
            public static class ChatSession
            {
                public static LoginClient? LoginClient { get; set; }
                public static LoginReply? LoginReply { get; set; }
            }
        }
        """;
    }

    public static string RenderGodotChatSession()
    {
        return """
        using Godot;
        using Shared.Contracts.Chat;
        using Client.Login;

        namespace Client.Chat
        {
            public partial class ChatSession : Node
            {
                public LoginClient? LoginClient { get; set; }
                public LoginReply? LoginReply { get; set; }
            }
        }
        """;
    }

    public static string RenderGodotThemeClass()
    {
        return """
        using Godot;

        namespace Client.Theme
        {
            public static class LakonaTheme
            {
                // Backgrounds
                public static Color BgBase => new(0.039f, 0.059f, 0.039f);      // #0A0F0A
                public static Color BgPanel => new(0.059f, 0.102f, 0.059f);       // #0F1A0F
                public static Color BgInput => new(0.020f, 0.039f, 0.039f);       // #050A0A
                public static Color BgHover => new(0.082f, 0.125f, 0.082f);       // #152015

                // Accent greens
                public static Color Accent => new(0f, 1f, 0.4f);                  // #00FF66
                public static Color AccentDim => new(0f, 0.667f, 0.267f);         // #00AA44
                public static Color AccentGlow => new(0.2f, 1f, 0.533f);          // #33FF88

                // Text
                public static Color TextPrimary => new(0f, 1f, 0.4f);            // #00FF66
                public static Color TextBody => new(0.533f, 0.8f, 0.6f);         // #88CC99
                public static Color TextDim => new(0.267f, 0.533f, 0.333f);      // #448855
                public static Color TextSystem => new(0.4f, 0.667f, 0.467f);     // #66AA77

                // Feedback
                public static Color Warning => new(1f, 1f, 0f);                  // #FFFF00
                public static Color Error => new(1f, 0.267f, 0.267f);            // #FF4444

                // Typography
                public const int FontSize = 14;
                public const int FontSizeTitle = 22;
                public const int FontSizeHeader = 18;
                public const int FontSizeSystem = 12;

                // Layout
                public static Vector2 ButtonMinSize => new(0, 36);
                public static Vector2 SendButtonMinSize => new(96, 36);
                public static Vector2 PanelMinSize => new(360, 0);
                public const int PanelSeparation = 12;
                public const int HeaderSeparation = 12;
                public const int FooterSeparation = 8;
                public const int SendRowSeparation = 8;
                public const int PageMargin = 16;
            }
        }
        """;
    }

    public static string RenderUnityLoginUI(NewCommandOptions options)
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
        using Shared.Contracts.Chat;
        using Client.Chat;
        using Lakona.Rpc.Client;
        using Lakona.Rpc.Core;
        {{serializerUsing}}
        {{transportUsing}}
        using UnityEngine;
        using UnityEngine.SceneManagement;
        using UnityEngine.UIElements;

        namespace Client.Login
        {
            [RequireComponent(typeof(UIDocument))]
            public sealed class LoginUI : MonoBehaviour
            {
                [SerializeField] private string _serverHost = "127.0.0.1";
                [SerializeField] private int _serverPort = 20000;
                [SerializeField] private string _serverPath = "{{TemplateText.SanitizeStringLiteral(defaultPath)}}";

                private CancellationTokenSource? _cts;
                private TextField? _nameField;
                private Button? _connectButton;
                private Label? _statusLabel;
                private bool _isConnecting;

                private void Start()
                {
                    var root = GetComponent<UIDocument>().rootVisualElement;

                    _nameField = root.Q<TextField>("name-field");
                    _connectButton = root.Q<Button>("connect-button");
                    _statusLabel = root.Q<Label>("status-label");

                    if (_connectButton != null)
                    {
                        _connectButton.clicked += OnConnectClicked;
                    }

                    _nameField?.RegisterCallback<KeyDownEvent>(evt =>
                    {
                        if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                        {
                            OnConnectClicked();
                        }
                    });

                    SetBusy(false);
                }

                private async void OnConnectClicked()
                {
                    if (_isConnecting)
                    {
                        return;
                    }

                    var name = _nameField?.value?.Trim();
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        SetStatus("Enter a name.");
                        return;
                    }

                    SetBusy(true);
                    SetStatus("Connecting...");
                    _cts = new CancellationTokenSource();

                    var client = new LoginClient(CreateRpcClientOptions());
                    client.OnDisconnected += () => Debug.Log("Disconnected from server.");

                    try
                    {
                        await client.ConnectAsync(_cts.Token);
                        var reply = await client.LoginAsync(name);
                        ChatSession.LoginClient = client;
                        ChatSession.LoginReply = reply;
                        SceneManager.LoadScene("ChatScene");
                    }
                    catch (Exception ex)
                    {
                        SetStatus($"Connection failed: {ex.Message}");
                        await client.DisposeAsync();
                    }
                    finally
                    {
                        SetBusy(false);
                    }
                }

                private void SetStatus(string text)
                {
                    if (_statusLabel != null)
                    {
                        _statusLabel.text = text;
                    }
                }

                private void SetBusy(bool isBusy)
                {
                    _isConnecting = isBusy;
                    if (_connectButton != null)
                    {
                        _connectButton.SetEnabled(!isBusy);
                        _connectButton.text = isBusy ? "Connecting..." : "Connect";
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

                private void OnDestroy()
                {
                    _cts?.Cancel();
                    _cts?.Dispose();
                }
            }
        }
        """;
    }

    public static string RenderUnityLoginUxml()
    {
        return """
        <ui:UXML xmlns:ui="UnityEngine.UIElements" xmlns:uie="UnityEditor.UIElements">
            <Style src="Theme/LakonaTheme.tss" />
            <Style src="LoginScene.uss" />
            <ui:VisualElement class="login-container">
                <ui:VisualElement class="scanline-overlay" />
                <ui:VisualElement class="login-panel">
                    <ui:Label text="LAKONA" class="login-title" />
                    <ui:Label text="NAME:" class="name-label" />
                    <ui:TextField name="name-field" max-length="20" class="name-field" />
                    <ui:Button text="CONNECT" name="connect-button" class="connect-button" />
                    <ui:Label text="" name="status-label" class="status-label" />
                </ui:VisualElement>
            </ui:VisualElement>
        </ui:UXML>
        """;
    }

    public static string RenderUnityLoginUss()
    {
        return """
        .login-container {
            width: 100%;
            height: 100%;
            flex-grow: 1;
            background-color: var(--lakona-bg-base);
            align-items: center;
            justify-content: center;
        }
        .scanline-overlay {
            position: absolute;
            top: 0;
            left: 0;
            right: 0;
            bottom: 0;
            background-color: rgba(0, 0, 0, 0);
            pointer-events: none;
        }
        .login-panel {
            width: 360px;
            padding: 32px 24px;
            background-color: var(--lakona-bg-panel);
            border-left-width: var(--lakona-border-width);
            border-right-width: var(--lakona-border-width);
            border-top-width: var(--lakona-border-width);
            border-bottom-width: var(--lakona-border-width);
            border-left-color: var(--lakona-accent);
            border-right-color: var(--lakona-accent);
            border-top-color: var(--lakona-accent);
            border-bottom-color: var(--lakona-accent);
        }
        .login-title {
            font-size: var(--lakona-font-size-title);
            -unity-font: var(--lakona-font);
            color: var(--lakona-accent);
            margin-bottom: 20px;
        }
        .name-label {
            font-size: var(--lakona-font-size);
            -unity-font: var(--lakona-font);
            color: var(--lakona-accent-dim);
            margin-bottom: 4px;
        }
        .name-field {
            margin-bottom: 16px;
        }
        .name-field .unity-text-field__label {
            color: var(--lakona-accent-dim);
            -unity-font: var(--lakona-font);
        }
        .name-field .unity-text-field__input {
            color: var(--lakona-accent);
            -unity-font: var(--lakona-font);
            font-size: var(--lakona-font-size);
            background-color: var(--lakona-bg-input);
            border-top-width: var(--lakona-border-width);
            border-right-width: var(--lakona-border-width);
            border-bottom-width: var(--lakona-border-width);
            border-left-width: var(--lakona-border-width);
            border-top-color: var(--lakona-accent-dim);
            border-right-color: var(--lakona-accent-dim);
            border-bottom-color: var(--lakona-accent-dim);
            border-left-color: var(--lakona-accent-dim);
        }
        .name-field .unity-text-field__input:focus {
            border-top-color: var(--lakona-accent);
            border-right-color: var(--lakona-accent);
            border-bottom-color: var(--lakona-accent);
            border-left-color: var(--lakona-accent);
        }
        .connect-button {
            width: 100%;
            font-size: var(--lakona-font-size);
            -unity-font: var(--lakona-font);
            color: var(--lakona-bg-base);
            background-color: var(--lakona-accent);
            border-top-width: var(--lakona-border-width);
            border-right-width: var(--lakona-border-width);
            border-bottom-width: var(--lakona-border-width);
            border-left-width: var(--lakona-border-width);
            border-top-color: var(--lakona-accent);
            border-right-color: var(--lakona-accent);
            border-bottom-color: var(--lakona-accent);
            border-left-color: var(--lakona-accent);
            margin-bottom: 12px;
        }
        .connect-button:disabled {
            color: var(--lakona-accent-dim);
            background-color: var(--lakona-bg-panel);
            border-top-color: var(--lakona-accent-dim);
            border-right-color: var(--lakona-accent-dim);
            border-bottom-color: var(--lakona-accent-dim);
            border-left-color: var(--lakona-accent-dim);
        }
        .status-label {
            font-size: var(--lakona-font-size);
            -unity-font: var(--lakona-font);
            color: var(--lakona-error);
            white-space: normal;
        }
        """;
    }

    public static string RenderGodotLoginScene(NewCommandOptions options)
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
        using Client.Chat;
        using Client.Theme;
        using Lakona.Rpc.Client;
        using Lakona.Rpc.Core;
        {{serializerUsing}}
        {{transportUsing}}

        namespace Client.Login
        {
            public partial class LoginScene : Control
            {
                [Export] private string _serverHost = "127.0.0.1";
                [Export] private int _serverPort = 20000;
                [Export] private string _serverPath = "{{TemplateText.SanitizeStringLiteral(defaultPath)}}";

                private readonly CancellationTokenSource _cts = new();
                private LineEdit? _nameField;
                private Button? _connectButton;
                private Label? _statusLabel;
                private bool _isConnecting;

                public override void _Ready()
                {
                    BuildUi();
                    SetBusy(false);
                }

                private void BuildUi()
                {
                    SetAnchorsPreset(LayoutPreset.FullRect);

                    var background = new ColorRect
                    {
                        Name = "Background",
                        Color = LakonaTheme.BgBase
                    };
                    background.SetAnchorsPreset(LayoutPreset.FullRect);
                    AddChild(background);

                    var scanlines = new ColorRect
                    {
                        Name = "Scanlines",
                        Color = new Color(0, 0, 0, 0.08f)
                    };
                    scanlines.SetAnchorsPreset(LayoutPreset.FullRect);
                    scanlines.MouseFilter = MouseFilterEnum.Ignore;
                    AddChild(scanlines);

                    var center = new CenterContainer { Name = "Center" };
                    center.SetAnchorsPreset(LayoutPreset.FullRect);
                    AddChild(center);

                    var panel = new PanelContainer { Name = "LoginPanel" };
                    panel.CustomMinimumSize = LakonaTheme.PanelMinSize;
                    center.AddChild(panel);

                    var panelBg = new StyleBoxFlat
                    {
                        BgColor = LakonaTheme.BgPanel,
                        BorderColor = LakonaTheme.Accent,
                        BorderWidthLeft = 2,
                        BorderWidthRight = 2,
                        BorderWidthTop = 2,
                        BorderWidthBottom = 2,
                        CornerRadiusTopLeft = 0,
                        CornerRadiusTopRight = 0,
                        CornerRadiusBottomLeft = 0,
                        CornerRadiusBottomRight = 0,
                        ContentMarginLeft = 24,
                        ContentMarginRight = 24,
                        ContentMarginTop = 32,
                        ContentMarginBottom = 32
                    };
                    panel.AddThemeStyleboxOverride("panel", panelBg);

                    var content = new VBoxContainer { Name = "PanelContent" };
                    content.AddThemeConstantOverride("separation", LakonaTheme.PanelSeparation);
                    panel.AddChild(content);

                    var title = new Label { Name = "Title", Text = "LAKONA" };
                    title.AddThemeFontSizeOverride("font_size", LakonaTheme.FontSizeTitle);
                    title.AddThemeColorOverride("font_color", LakonaTheme.Accent);
                    content.AddChild(title);

                    var nameLabel = new Label { Name = "NameLabel", Text = "NAME:" };
                    nameLabel.AddThemeFontSizeOverride("font_size", LakonaTheme.FontSize);
                    nameLabel.AddThemeColorOverride("font_color", LakonaTheme.AccentDim);
                    content.AddChild(nameLabel);

                    _nameField = new LineEdit { Name = "NameField", PlaceholderText = "", MaxLength = 20 };
                    _nameField.CustomMinimumSize = LakonaTheme.ButtonMinSize;
                    _nameField.AddThemeColorOverride("font_color", LakonaTheme.Accent);
                    _nameField.AddThemeColorOverride("font_placeholder_color", LakonaTheme.TextDim);
                    _nameField.TextSubmitted += _ => OnConnectPressed();
                    content.AddChild(_nameField);

                    _connectButton = new Button { Name = "ConnectButton", Text = "CONNECT" };
                    _connectButton.CustomMinimumSize = LakonaTheme.ButtonMinSize;
                    _connectButton.AddThemeColorOverride("font_color", LakonaTheme.BgBase);
                    _connectButton.AddThemeColorOverride("font_disabled_color", LakonaTheme.AccentDim);
                    _connectButton.Pressed += OnConnectPressed;
                    content.AddChild(_connectButton);

                    _statusLabel = new Label { Name = "StatusLabel", Text = "" };
                    _statusLabel.AddThemeFontSizeOverride("font_size", LakonaTheme.FontSize);
                    _statusLabel.AddThemeColorOverride("font_color", LakonaTheme.Error);
                    content.AddChild(_statusLabel);
                }

                private async void OnConnectPressed()
                {
                    if (_isConnecting)
                    {
                        return;
                    }

                    var name = _nameField?.Text.Trim();
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        SetStatus("Enter a name.");
                        _nameField?.GrabFocus();
                        return;
                    }

                    SetBusy(true);
                    SetStatus("Connecting...");

                    var client = new LoginClient(CreateRpcClientOptions());
                    client.OnDisconnected += () => GD.Print("Disconnected from server.");

                    try
                    {
                        await client.ConnectAsync(_cts.Token);
                        var reply = await client.LoginAsync(name);
                        var session = GetNode<ChatSession>("/root/ChatSession");
                        session.LoginClient = client;
                        session.LoginReply = reply;
                        session.ConnectionId = reply.ConnectionId;
                        GetTree().ChangeSceneToFile("res://Chat.tscn");
                    }
                    catch (Exception ex)
                    {
                        SetStatus($"Connection failed: {ex.Message}");
                        await client.DisposeAsync();
                    }
                    finally
                    {
                        SetBusy(false);
                    }
                }

                private void SetStatus(string text)
                {
                    if (_statusLabel != null)
                    {
                        _statusLabel.Text = text;
                    }
                }

                private void SetBusy(bool isBusy)
                {
                    _isConnecting = isBusy;
                    if (_connectButton != null)
                    {
                        _connectButton.Disabled = isBusy;
                        _connectButton.Text = isBusy ? "CONNECTING..." : "CONNECT";
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

                public override void _ExitTree()
                {
                    _cts.Cancel();
                    _cts.Dispose();
                }
            }
        }
        """;
    }

    public static string RenderGodotLoginTscn()
    {
        return """
        [gd_scene load_steps=2 format=3]

        [ext_resource type="Script" path="res://Scripts/Login/LoginScene.cs" id="1"]

        [node name="LoginScene" type="Control"]
        layout_mode = 3
        anchors_preset = 15
        anchor_right = 1.0
        anchor_bottom = 1.0
        grow_horizontal = 2
        grow_vertical = 2
        script = ExtResource("1")
        """;
    }

    public static string RenderGodotChatScene()
    {
        return """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Godot;
        using Shared.Contracts.Chat;
        using Client.Chat;
        using Client.Theme;

        namespace Client.Chat
        {
            public partial class ChatScene : Control
            {
                private readonly CancellationTokenSource _cts = new();
                private LoginClient? _loginClient;
                private ChatClient? _client;
                private LineEdit? _messageField;
                private Button? _sendButton;
                private RichTextLabel? _messageLog;
                private Label? _onlineCount;
                private bool _isSending;

                public override void _Ready()
                {
                    BuildUi();

                    var session = GetNode<ChatSession>("/root/ChatSession");
                    var loginClient = session.LoginClient;
                    var loginReply = session.LoginReply;

                    if (loginClient == null)
                    {
                        AppendSystemMessage("Session expired. Please return to login.");
                        SetSendBusy(true);
                        return;
                    }

                    _loginClient = loginClient;
                    _client = new ChatClient(loginClient);

                    _client.OnMessageReceived += msg => CallDeferred(nameof(AppendMessageDeferred), msg.SenderName, msg.Text);
                    loginClient.OnUserJoined += member => CallDeferred(nameof(AppendSystemMessageDeferred), $"{member.Name} joined.");
                    loginClient.OnUserLeft += memberName => CallDeferred(nameof(AppendSystemMessageDeferred), $"{memberName} left.");
                    loginClient.OnDisconnected += () => CallDeferred(nameof(AppendSystemMessageDeferred), "Disconnected from server.");

                    if (loginReply != null)
                    {
                        AppendSystemMessage($"Connected. {loginReply.Members.Count} online.");
                        SetOnlineCount(loginReply.Members.Count);

                        foreach (var msg in loginReply.RecentMessages)
                        {
                            AppendMessageText(msg.SenderName, msg.Text);
                        }
                    }

                    SetSendBusy(true);
                    _ = BindChatAsync();
                }

                private void BuildUi()
                {
                    SetAnchorsPreset(LayoutPreset.FullRect);

                    var background = new ColorRect
                    {
                        Name = "Background",
                        Color = LakonaTheme.BgBase
                    };
                    background.SetAnchorsPreset(LayoutPreset.FullRect);
                    AddChild(background);

                    var scanlines = new ColorRect
                    {
                        Name = "Scanlines",
                        Color = new Color(0, 0, 0, 0.08f)
                    };
                    scanlines.SetAnchorsPreset(LayoutPreset.FullRect);
                    scanlines.MouseFilter = MouseFilterEnum.Ignore;
                    AddChild(scanlines);

                    var margin = new MarginContainer { Name = "Layout" };
                    margin.SetAnchorsPreset(LayoutPreset.FullRect);
                    margin.AddThemeConstantOverride("margin_left", LakonaTheme.PageMargin);
                    margin.AddThemeConstantOverride("margin_top", LakonaTheme.PageMargin);
                    margin.AddThemeConstantOverride("margin_right", LakonaTheme.PageMargin);
                    margin.AddThemeConstantOverride("margin_bottom", LakonaTheme.PageMargin);
                    AddChild(margin);

                    var layout = new VBoxContainer { Name = "ChatLayout" };
                    layout.AddThemeConstantOverride("separation", 0);
                    margin.AddChild(layout);

                    // Header
                    var header = new PanelContainer { Name = "Header" };
                    var headerBg = new StyleBoxFlat
                    {
                        BgColor = LakonaTheme.BgPanel,
                        BorderColor = LakonaTheme.Accent,
                        BorderWidthBottom = 2,
                        BorderWidthLeft = 0,
                        BorderWidthRight = 0,
                        BorderWidthTop = 0,
                        CornerRadiusTopLeft = 0,
                        CornerRadiusTopRight = 0,
                        CornerRadiusBottomLeft = 0,
                        CornerRadiusBottomRight = 0,
                        ContentMarginLeft = 0,
                        ContentMarginRight = 0,
                        ContentMarginTop = 8,
                        ContentMarginBottom = 8
                    };
                    header.AddThemeStyleboxOverride("panel", headerBg);
                    layout.AddChild(header);

                    var headerRow = new HBoxContainer { Name = "HeaderRow" };
                    headerRow.AddThemeConstantOverride("separation", LakonaTheme.HeaderSeparation);
                    header.AddChild(headerRow);

                    var title = new Label { Name = "Title", Text = "CHAT ROOM" };
                    title.AddThemeFontSizeOverride("font_size", LakonaTheme.FontSizeHeader);
                    title.AddThemeColorOverride("font_color", LakonaTheme.Accent);
                    title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                    headerRow.AddChild(title);

                    _onlineCount = new Label { Name = "OnlineCount", Text = "ONLINE: --" };
                    _onlineCount.AddThemeFontSizeOverride("font_size", LakonaTheme.FontSize);
                    _onlineCount.AddThemeColorOverride("font_color", LakonaTheme.Warning);
                    headerRow.AddChild(_onlineCount);

                    // Message log
                    _messageLog = new RichTextLabel
                    {
                        Name = "MessageLog",
                        BbcodeEnabled = false,
                        ScrollFollowing = true
                    };
                    _messageLog.AddThemeColorOverride("default_color", LakonaTheme.TextBody);
                    _messageLog.AddThemeFontSizeOverride("normal_font_size", LakonaTheme.FontSize);
                    _messageLog.SizeFlagsVertical = SizeFlags.ExpandFill;
                    layout.AddChild(_messageLog);

                    // Footer
                    var footer = new PanelContainer { Name = "Footer" };
                    var footerBg = new StyleBoxFlat
                    {
                        BgColor = LakonaTheme.BgPanel,
                        BorderColor = LakonaTheme.Accent,
                        BorderWidthTop = 2,
                        BorderWidthLeft = 0,
                        BorderWidthRight = 0,
                        BorderWidthBottom = 0,
                        CornerRadiusTopLeft = 0,
                        CornerRadiusTopRight = 0,
                        CornerRadiusBottomLeft = 0,
                        CornerRadiusBottomRight = 0,
                        ContentMarginLeft = 0,
                        ContentMarginRight = 0,
                        ContentMarginTop = 8,
                        ContentMarginBottom = 8
                    };
                    footer.AddThemeStyleboxOverride("panel", footerBg);
                    layout.AddChild(footer);

                    var sendRow = new HBoxContainer { Name = "SendRow" };
                    sendRow.AddThemeConstantOverride("separation", LakonaTheme.SendRowSeparation);
                    footer.AddChild(sendRow);

                    var msgLabel = new Label { Name = "MessageLabel", Text = "MESSAGE:" };
                    msgLabel.AddThemeFontSizeOverride("font_size", LakonaTheme.FontSize);
                    msgLabel.AddThemeColorOverride("font_color", LakonaTheme.AccentDim);
                    sendRow.AddChild(msgLabel);

                    _messageField = new LineEdit { Name = "MessageField", PlaceholderText = "", MaxLength = 500 };
                    _messageField.CustomMinimumSize = LakonaTheme.ButtonMinSize;
                    _messageField.AddThemeColorOverride("font_color", LakonaTheme.Accent);
                    _messageField.AddThemeColorOverride("font_placeholder_color", LakonaTheme.TextDim);
                    _messageField.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                    _messageField.TextSubmitted += _ => OnSendPressed();
                    sendRow.AddChild(_messageField);

                    _sendButton = new Button { Name = "SendButton", Text = "SEND" };
                    _sendButton.CustomMinimumSize = LakonaTheme.SendButtonMinSize;
                    _sendButton.AddThemeColorOverride("font_color", LakonaTheme.BgBase);
                    _sendButton.AddThemeColorOverride("font_disabled_color", LakonaTheme.AccentDim);
                    _sendButton.Pressed += OnSendPressed;
                    sendRow.AddChild(_sendButton);
                }

                private async void OnSendPressed()
                {
                    if (_isSending)
                    {
                        return;
                    }

                    if (_loginClient == null || !_loginClient.IsConnected)
                    {
                        AppendSystemMessage("Not connected.");
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
                        _onlineCount.Text = $"ONLINE: {count}";
                    }
                }

                private void SetSendBusy(bool isBusy)
                {
                    _isSending = isBusy;
                    if (_sendButton != null)
                    {
                        _sendButton.Disabled = isBusy;
                        _sendButton.Text = isBusy ? "SENDING..." : "SEND";
                    }
                }

                private async Task BindChatAsync()
                {
                    if (_client == null)
                    {
                        return;
                    }

                    try
                    {
                        await _client.BindAsync();
                        CallDeferred(nameof(SetSendBusyDeferred), false);
                    }
                    catch (Exception ex)
                    {
                        CallDeferred(nameof(AppendSystemMessageDeferred), $"Chat bind failed: {ex.Message}");
                    }
                }

                public void SetSendBusyDeferred(bool isBusy)
                {
                    SetSendBusy(isBusy);
                }

                public override void _ExitTree()
                {
                    _cts.Cancel();
                    var session = GetNode<ChatSession>("/root/ChatSession");
                    if (session?.LoginClient is not null)
                    {
                        _ = session.LoginClient.DisposeAsync();
                    }
                    _cts.Dispose();
                }
            }
        }
        """;
    }

    public static string RenderGodotChatTscn()
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

    public static string RenderClientChatUI()
    {
        return """
        using System;
        using System.Collections.Concurrent;
        using System.Threading;
        using System.Threading.Tasks;
        using Shared.Contracts.Chat;
        using Client.Chat;
        using UnityEngine;
        using UnityEngine.UIElements;

        namespace Client.Chat
        {
            [RequireComponent(typeof(UIDocument))]
            public sealed class ChatUI : MonoBehaviour
            {
                private readonly CancellationTokenSource _cts = new();
                private readonly ConcurrentQueue<Action> _mainThreadActions = new();
                private LoginClient? _loginClient;
                private ChatClient? _client;
                private TextField? _inputField;
                private ScrollView? _messageList;
                private Label? _onlineCount;
                private Button? _sendButton;
                private bool _isSending;

                private void Start()
                {
                    var root = GetComponent<UIDocument>().rootVisualElement;

                    _inputField = root.Q<TextField>("chat-input");
                    _messageList = root.Q<ScrollView>("message-list");
                    _onlineCount = root.Q<Label>("online-count");
                    _sendButton = root.Q<Button>("send-button");

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

                    var loginClient = ChatSession.LoginClient;
                    var loginReply = ChatSession.LoginReply;

                    if (loginClient == null)
                    {
                        AppendSystemMessage("Session expired. Please return to login.");
                        SetSendBusy(true);
                        return;
                    }

                    _loginClient = loginClient;
                    _client = new ChatClient(loginClient);

                    _client.OnMessageReceived += msg => EnqueueMainThread(() => AppendMessage(msg));
                    loginClient.OnUserJoined += member => EnqueueMainThread(() => OnUserJoinedHandler(member));
                    loginClient.OnUserLeft += memberName => EnqueueMainThread(() => OnUserLeftHandler(memberName));
                    loginClient.OnDisconnected += () => EnqueueMainThread(() => AppendSystemMessage("Disconnected from server."));

                    if (loginReply != null)
                    {
                        AppendSystemMessage($"Connected. {loginReply.Members.Count} online.");
                        SetOnlineCount(loginReply.Members.Count);

                        foreach (var msg in loginReply.RecentMessages)
                        {
                            AppendMessage(msg);
                        }
                    }

                    SetSendBusy(true);
                    _ = BindChatAsync();
                }

                private async void OnSendClicked()
                {
                    if (_isSending)
                    {
                        return;
                    }

                    if (_loginClient == null || !_loginClient.IsConnected)
                    {
                        AppendSystemMessage("Not connected.");
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

                private async Task BindChatAsync()
                {
                    if (_client == null)
                    {
                        return;
                    }

                    try
                    {
                        await _client.BindAsync();
                        EnqueueMainThread(() => SetSendBusy(false));
                    }
                    catch (Exception ex)
                    {
                        EnqueueMainThread(() => AppendSystemMessage($"Chat bind failed: {ex.Message}"));
                    }
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

                private void SetSendBusy(bool isBusy)
                {
                    _isSending = isBusy;
                    if (_sendButton != null)
                    {
                        _sendButton.SetEnabled(!isBusy);
                        _sendButton.text = isBusy ? "Sending..." : "Send";
                    }
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
                    if (ChatSession.LoginClient is not null)
                    {
                        _ = ChatSession.LoginClient.DisposeAsync();
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
            <ui:VisualElement class="chat-container">
                <ui:VisualElement class="chat-header">
                    <ui:Label text="Chat Room" class="header-title" />
                    <ui:Label text="Online: --" name="online-count" class="header-count" />
                </ui:VisualElement>
                <ui:ScrollView name="message-list" class="message-list" />
                <ui:VisualElement class="chat-footer">
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
            flex-direction: row;
            padding: 8px;
            background-color: rgb(40, 40, 40);
            border-top-width: 1px;
            border-top-color: rgb(60, 60, 60);
        }
        .chat-input {
            flex-grow: 1;
            margin-right: 8px;
        }
        .chat-input .unity-text-field__label {
            color: rgb(210, 210, 210);
        }
        .chat-input .unity-text-field__input {
            color: rgb(245, 245, 245);
            background-color: rgb(24, 24, 24);
            border-top-color: rgb(80, 80, 80);
            border-right-color: rgb(80, 80, 80);
            border-bottom-color: rgb(80, 80, 80);
            border-left-color: rgb(80, 80, 80);
        }
        .send-button {
            width: 80px;
            color: rgb(245, 245, 245);
            background-color: rgb(54, 94, 160);
            border-top-color: rgb(86, 132, 210);
            border-right-color: rgb(86, 132, 210);
            border-bottom-color: rgb(86, 132, 210);
            border-left-color: rgb(86, 132, 210);
        }
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
}
