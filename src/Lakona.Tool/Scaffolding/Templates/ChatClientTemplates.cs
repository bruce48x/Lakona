internal static class ChatClientTemplates
{
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
}
