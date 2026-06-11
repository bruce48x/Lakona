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

    public static string RenderGodotTheme()
    {
        return """
        [gd_resource type="Theme" load_steps=8 format=3]

        [sub_resource type="StyleBoxFlat" id="1"]
        bg_color = Color(0.02, 0.039, 0.039, 1)
        border_width_left = 2
        border_width_right = 2
        border_width_top = 2
        border_width_bottom = 2
        border_color = Color(0, 0.667, 0.267, 1)
        corner_radius_top_left = 0
        corner_radius_top_right = 0
        corner_radius_bottom_left = 0
        corner_radius_bottom_right = 0
        content_margin_left = 8.0
        content_margin_right = 8.0
        content_margin_top = 4.0
        content_margin_bottom = 4.0

        [sub_resource type="StyleBoxFlat" id="2"]
        bg_color = Color(0, 1, 0.4, 1)
        corner_radius_top_left = 0
        corner_radius_top_right = 0
        corner_radius_bottom_left = 0
        corner_radius_bottom_right = 0
        content_margin_left = 8.0
        content_margin_right = 8.0
        content_margin_top = 4.0
        content_margin_bottom = 4.0

        [sub_resource type="StyleBoxFlat" id="3"]
        bg_color = Color(0.059, 0.102, 0.059, 1)
        border_width_left = 2
        border_width_right = 2
        border_width_top = 2
        border_width_bottom = 2
        border_color = Color(0, 0.667, 0.267, 1)
        corner_radius_top_left = 0
        corner_radius_top_right = 0
        corner_radius_bottom_left = 0
        corner_radius_bottom_right = 0
        content_margin_left = 8.0
        content_margin_right = 8.0
        content_margin_top = 4.0
        content_margin_bottom = 4.0

        [sub_resource type="StyleBoxFlat" id="4"]
        bg_color = Color(0.2, 1, 0.533, 1)
        corner_radius_top_left = 0
        corner_radius_top_right = 0
        corner_radius_bottom_left = 0
        corner_radius_bottom_right = 0
        content_margin_left = 8.0
        content_margin_right = 8.0
        content_margin_top = 4.0
        content_margin_bottom = 4.0

        [sub_resource type="StyleBoxFlat" id="5"]
        bg_color = Color(0.059, 0.102, 0.059, 1)
        border_width_left = 2
        border_width_right = 2
        border_width_top = 2
        border_width_bottom = 2
        border_color = Color(0, 1, 0.4, 1)
        corner_radius_top_left = 0
        corner_radius_top_right = 0
        corner_radius_bottom_left = 0
        corner_radius_bottom_right = 0
        content_margin_left = 24.0
        content_margin_right = 24.0
        content_margin_top = 32.0
        content_margin_bottom = 32.0

        [sub_resource type="StyleBoxFlat" id="6"]
        bg_color = Color(0.059, 0.102, 0.059, 1)
        border_width_bottom = 2
        border_color = Color(0, 1, 0.4, 1)
        corner_radius_top_left = 0
        corner_radius_top_right = 0
        corner_radius_bottom_left = 0
        corner_radius_bottom_right = 0
        content_margin_top = 8.0
        content_margin_bottom = 8.0

        [sub_resource type="StyleBoxFlat" id="7"]
        bg_color = Color(0.059, 0.102, 0.059, 1)
        border_width_top = 2
        border_color = Color(0, 1, 0.4, 1)
        corner_radius_top_left = 0
        corner_radius_top_right = 0
        corner_radius_bottom_left = 0
        corner_radius_bottom_right = 0
        content_margin_top = 8.0
        content_margin_bottom = 8.0

        [resource]
        default_font_size = 14

        Button/colors/font_color = Color(0.039, 0.059, 0.039, 1)
        Button/colors/font_disabled_color = Color(0, 0.667, 0.267, 1)
        Button/styles/normal = SubResource("2")
        Button/styles/disabled = SubResource("3")
        Button/styles/hover = SubResource("4")

        LineEdit/colors/font_color = Color(0, 1, 0.4, 1)
        LineEdit/colors/font_placeholder_color = Color(0.267, 0.533, 0.333, 1)
        LineEdit/styles/normal = SubResource("1")

        Label/colors/font_color = Color(0.533, 0.8, 0.6, 1)
        Label/font_sizes/font_size = 14

        RichTextLabel/colors/default_color = Color(0.533, 0.8, 0.6, 1)
        RichTextLabel/font_sizes/normal_font_size = 14

        TitleLabel/colors/font_color = Color(0, 1, 0.4, 1)
        TitleLabel/font_sizes/font_size = 22

        HeaderLabel/colors/font_color = Color(0, 1, 0.4, 1)
        HeaderLabel/font_sizes/font_size = 18

        NameLabel/colors/font_color = Color(0, 0.667, 0.267, 1)
        NameLabel/font_sizes/font_size = 14

        StatusLabel/colors/font_color = Color(1, 0.267, 0.267, 1)
        StatusLabel/font_sizes/font_size = 14

        OnlineCount/colors/font_color = Color(1, 1, 0, 1)
        OnlineCount/font_sizes/font_size = 14

        PanelVBox/constants/separation = 12
        ChatVBox/constants/separation = 0
        HeaderRow/constants/separation = 12
        SendRow/constants/separation = 8

        PageMargin/constants/margin_left = 16
        PageMargin/constants/margin_right = 16
        PageMargin/constants/margin_top = 16
        PageMargin/constants/margin_bottom = 16

        LoginPanel/styles/panel = SubResource("5")
        ChatHeader/styles/panel = SubResource("6")
        ChatFooter/styles/panel = SubResource("7")
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
                        _connectButton.text = isBusy ? "CONNECTING..." : "CONNECT";
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
            <Style src="LoginScene.uss" />
            <ui:VisualElement class="login-container">
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
            background-color: var(--lakona-bg-input);
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
                    _nameField = GetNode<LineEdit>("%NameField");
                    _connectButton = GetNode<Button>("%ConnectButton");
                    _statusLabel = GetNode<Label>("%StatusLabel");

                    _nameField.TextSubmitted += _ => OnConnectPressed();
                    _connectButton.Pressed += OnConnectPressed;

                    SetBusy(false);
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
        [gd_scene load_steps=3 format=3]

        [ext_resource type="Script" path="res://Scripts/Login/LoginScene.cs" id="1"]
        [ext_resource type="Theme" path="res://Theme/LakonaTheme.tres" id="2"]

        [node name="LoginScene" type="Control"]
        layout_mode = 3
        anchors_preset = 15
        anchor_right = 1.0
        anchor_bottom = 1.0
        grow_horizontal = 2
        grow_vertical = 2
        theme = ExtResource("2")
        script = ExtResource("1")

        [node name="Background" type="ColorRect" parent="."]
        layout_mode = 1
        anchors_preset = 15
        anchor_right = 1.0
        anchor_bottom = 1.0
        grow_horizontal = 2
        grow_vertical = 2
        color = Color(0.039, 0.059, 0.039, 1)

        [node name="Scanlines" type="ColorRect" parent="."]
        layout_mode = 1
        anchors_preset = 15
        anchor_right = 1.0
        anchor_bottom = 1.0
        grow_horizontal = 2
        grow_vertical = 2
        color = Color(0, 0, 0, 0.08)
        mouse_filter = 2

        [node name="Center" type="CenterContainer" parent="."]
        layout_mode = 1
        anchors_preset = 15
        anchor_right = 1.0
        anchor_bottom = 1.0
        grow_horizontal = 2
        grow_vertical = 2

        [node name="LoginPanel" type="PanelContainer" parent="Center"]
        layout_mode = 0
        theme_type_variation = LoginPanel
        custom_minimum_size = Vector2(360, 0)

        [node name="PanelContent" type="VBoxContainer" parent="Center/LoginPanel"]
        layout_mode = 0
        theme_type_variation = PanelVBox

        [node name="Title" type="Label" parent="Center/LoginPanel/PanelContent"]
        layout_mode = 0
        theme_type_variation = TitleLabel
        text = "LAKONA"

        [node name="NameLabel" type="Label" parent="Center/LoginPanel/PanelContent"]
        layout_mode = 0
        theme_type_variation = NameLabel
        text = "NAME:"

        [node name="NameField" type="LineEdit" parent="Center/LoginPanel/PanelContent"]
        layout_mode = 0
        max_length = 20
        custom_minimum_size = Vector2(0, 36)
        unique_name_in_owner = true

        [node name="ConnectButton" type="Button" parent="Center/LoginPanel/PanelContent"]
        layout_mode = 0
        text = "CONNECT"
        custom_minimum_size = Vector2(0, 36)
        unique_name_in_owner = true

        [node name="StatusLabel" type="Label" parent="Center/LoginPanel/PanelContent"]
        layout_mode = 0
        theme_type_variation = StatusLabel
        unique_name_in_owner = true
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
        using Client.Login;

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
                    _messageField = GetNode<LineEdit>("%MessageField");
                    _sendButton = GetNode<Button>("%SendButton");
                    _messageLog = GetNode<RichTextLabel>("%MessageLog");
                    _onlineCount = GetNode<Label>("%OnlineCount");

                    _messageField.TextSubmitted += _ => OnSendPressed();
                    _sendButton.Pressed += OnSendPressed;

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
        [gd_scene load_steps=3 format=3]

        [ext_resource type="Script" path="res://Scripts/Chat/ChatScene.cs" id="1"]
        [ext_resource type="Theme" path="res://Theme/LakonaTheme.tres" id="2"]

        [node name="ChatScene" type="Control"]
        layout_mode = 3
        anchors_preset = 15
        anchor_right = 1.0
        anchor_bottom = 1.0
        grow_horizontal = 2
        grow_vertical = 2
        theme = ExtResource("2")
        script = ExtResource("1")

        [node name="Background" type="ColorRect" parent="."]
        layout_mode = 1
        anchors_preset = 15
        anchor_right = 1.0
        anchor_bottom = 1.0
        grow_horizontal = 2
        grow_vertical = 2
        color = Color(0.039, 0.059, 0.039, 1)

        [node name="Scanlines" type="ColorRect" parent="."]
        layout_mode = 1
        anchors_preset = 15
        anchor_right = 1.0
        anchor_bottom = 1.0
        grow_horizontal = 2
        grow_vertical = 2
        color = Color(0, 0, 0, 0.08)
        mouse_filter = 2

        [node name="Layout" type="MarginContainer" parent="."]
        layout_mode = 1
        anchors_preset = 15
        anchor_right = 1.0
        anchor_bottom = 1.0
        grow_horizontal = 2
        grow_vertical = 2
        theme_type_variation = PageMargin

        [node name="ChatLayout" type="VBoxContainer" parent="Layout"]
        layout_mode = 0
        theme_type_variation = ChatVBox

        [node name="Header" type="PanelContainer" parent="Layout/ChatLayout"]
        layout_mode = 0
        theme_type_variation = ChatHeader

        [node name="HeaderRow" type="HBoxContainer" parent="Layout/ChatLayout/Header"]
        layout_mode = 0
        theme_type_variation = HeaderRow

        [node name="Title" type="Label" parent="Layout/ChatLayout/Header/HeaderRow"]
        layout_mode = 0
        theme_type_variation = HeaderLabel
        text = "CHAT ROOM"
        size_flags_horizontal = 3

        [node name="OnlineCount" type="Label" parent="Layout/ChatLayout/Header/HeaderRow"]
        layout_mode = 0
        theme_type_variation = OnlineCount
        text = "ONLINE: --"
        unique_name_in_owner = true

        [node name="MessageLog" type="RichTextLabel" parent="Layout/ChatLayout"]
        layout_mode = 0
        bbcode_enabled = false
        scroll_following = true
        size_flags_vertical = 3
        unique_name_in_owner = true

        [node name="Footer" type="PanelContainer" parent="Layout/ChatLayout"]
        layout_mode = 0
        theme_type_variation = ChatFooter

        [node name="SendRow" type="HBoxContainer" parent="Layout/ChatLayout/Footer"]
        layout_mode = 0
        theme_type_variation = SendRow

        [node name="MessageLabel" type="Label" parent="Layout/ChatLayout/Footer/SendRow"]
        layout_mode = 0
        theme_type_variation = NameLabel
        text = "MESSAGE:"

        [node name="MessageField" type="LineEdit" parent="Layout/ChatLayout/Footer/SendRow"]
        layout_mode = 0
        max_length = 500
        custom_minimum_size = Vector2(0, 36)
        size_flags_horizontal = 3
        unique_name_in_owner = true

        [node name="SendButton" type="Button" parent="Layout/ChatLayout/Footer/SendRow"]
        layout_mode = 0
        text = "SEND"
        custom_minimum_size = Vector2(96, 36)
        unique_name_in_owner = true
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
        using Client.Login;
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
                        _onlineCount.text = $"ONLINE: {count}";
                    }
                }

                private void SetSendBusy(bool isBusy)
                {
                    _isSending = isBusy;
                    if (_sendButton != null)
                    {
                        _sendButton.SetEnabled(!isBusy);
                        _sendButton.text = isBusy ? "SENDING..." : "SEND";
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
                    <ui:Label text="CHAT ROOM" class="header-title" />
                    <ui:Label text="ONLINE: --" name="online-count" class="header-count" />
                </ui:VisualElement>
                <ui:ScrollView name="message-list" class="message-list" />
                <ui:VisualElement class="chat-footer">
                    <ui:Label text="MESSAGE:" class="message-label" />
                    <ui:TextField name="chat-input" max-length="500" class="chat-input" />
                    <ui:Button text="SEND" name="send-button" class="send-button" />
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
            background-color: var(--lakona-bg-base);
        }
        .chat-header {
            flex-direction: row;
            align-items: center;
            padding: 8px 16px;
            background-color: var(--lakona-bg-panel);
            border-bottom-width: var(--lakona-border-width);
            border-bottom-color: var(--lakona-accent);
        }
        .header-title {
            font-size: var(--lakona-font-size-header);
            -unity-font: var(--lakona-font);
            color: var(--lakona-accent);
            flex-grow: 1;
        }
        .header-count {
            font-size: var(--lakona-font-size);
            -unity-font: var(--lakona-font);
            color: var(--lakona-warning);
        }
        .message-list {
            flex-grow: 1;
            padding: 8px 16px;
        }
        .chat-message {
            font-size: var(--lakona-font-size);
            -unity-font: var(--lakona-font);
            color: var(--lakona-text-body);
            margin-bottom: 4px;
            white-space: normal;
        }
        .chat-system {
            font-size: var(--lakona-font-size-system);
            -unity-font: var(--lakona-font);
            color: var(--lakona-text-system);
            -unity-font-style: italic;
            margin-bottom: 4px;
            white-space: normal;
        }
        .chat-footer {
            flex-direction: row;
            align-items: center;
            padding: 8px 16px;
            background-color: var(--lakona-bg-panel);
            border-top-width: var(--lakona-border-width);
            border-top-color: var(--lakona-accent);
        }
        .message-label {
            font-size: var(--lakona-font-size);
            -unity-font: var(--lakona-font);
            color: var(--lakona-accent-dim);
            margin-right: 8px;
        }
        .chat-input {
            flex-grow: 1;
            margin-right: 8px;
        }
        .chat-input .unity-text-field__label {
            display: none;
        }
        .chat-input .unity-text-field__input {
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
        .chat-input .unity-text-field__input:focus {
            border-top-color: var(--lakona-accent);
            border-right-color: var(--lakona-accent);
            border-bottom-color: var(--lakona-accent);
            border-left-color: var(--lakona-accent);
        }
        .send-button {
            width: 96px;
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
        }
        .send-button:disabled {
            color: var(--lakona-accent-dim);
            background-color: var(--lakona-bg-input);
            border-top-color: var(--lakona-accent-dim);
            border-right-color: var(--lakona-accent-dim);
            border-bottom-color: var(--lakona-accent-dim);
            border-left-color: var(--lakona-accent-dim);
        }
        """;
    }
}
