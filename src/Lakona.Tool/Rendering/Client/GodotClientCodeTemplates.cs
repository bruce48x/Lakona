using Lakona.Tool.Domain;

namespace Lakona.Tool.Rendering.Client;

internal static class GodotClientCodeTemplates
{
    public static string RenderGameClient()
    {
        return """
        using System;
        using System.Collections.Concurrent;
        using System.Threading;
        using System.Threading.Tasks;
        using Client.Generated;
        using Lakona.Game.Client;
        using Shared.Contracts.Game;

        namespace Client.Game;

        public sealed class GameClient : IGameCallback, IAsyncDisposable
        {
            private readonly LakonaGameClient _client;
            private readonly ConcurrentQueue<WorldSnapshot> _snapshots = new();
            private int _disconnected;

            public GameClient(LakonaGameClientOptions options)
            {
                _client = new LakonaGameClient(options, this);
                _client.Disconnected += _ => Interlocked.Exchange(ref _disconnected, 1);
            }

            public async Task ConnectAsync(CancellationToken cancellationToken) => await _client.ConnectAsync(cancellationToken);

            public async Task<LoginReply> LoginAsync(string playerName) =>
                await _client.Api.Shared.Game.LoginAsync(new LoginRequest { PlayerName = playerName });

            public ValueTask SubmitInputAsync(float x, float y) =>
                _client.Api.Shared.Game.SubmitInputAsync(new PlayerInput { DirectionX = x, DirectionY = y });

            public async ValueTask RefreshWorldAsync() =>
                _snapshots.Enqueue(await _client.Api.Shared.Game.GetWorldAsync(new WorldQuery()));

            public bool TryDequeueSnapshot(out WorldSnapshot snapshot) => _snapshots.TryDequeue(out snapshot!);
            public bool ConsumeDisconnected() => Interlocked.Exchange(ref _disconnected, 0) != 0;
            public ValueTask DisposeAsync() => _client.DisposeAsync();

            void IGameCallback.OnWorldUpdated(WorldSnapshot snapshot) => _snapshots.Enqueue(snapshot);
        }
        """;
    }

    public static string RenderGameScene(LakonaProjectSpec spec)
    {
        var transportUsing = RenderTransportUsing(spec.Transport);
        var serializerUsing = RenderSerializerUsing(spec.Serializer);
        var transportExpression = RenderTransportExpression(spec.Transport);
        var serializerExpression = RenderSerializerExpression(spec.Serializer);
        var defaultPath = spec.Transport == TransportKind.WebSocket ? "/ws" : string.Empty;

        return $$"""
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Godot;
        using Lakona.Game.Client;
        using Lakona.Rpc.Client;
        using Lakona.Rpc.Core;
        using Shared.Contracts.Game;
        {{serializerUsing}}
        {{transportUsing}}

        namespace Client.Game;

        public partial class GameScene : Node2D
        {
            [Export] private string _serverHost = "127.0.0.1";
            [Export] private int _serverPort = 20000;
            [Export] private string _serverPath = "{{defaultPath}}";

            private LineEdit _nameField = null!;
            private Button _connectButton = null!;
            private Label _statusLabel = null!;
            private Control _loginPanel = null!;
            private Control _hud = null!;
            private Label _playerLabel = null!;
            private Label _scoreLabel = null!;
            private Label _healthLabel = null!;
            private CancellationTokenSource? _cts;
            private GameClient? _client;
            private WorldSnapshot? _world;
            private long _localPlayerId;
            private bool _loginPending;
            private bool _inputPending;
            private bool _snapshotPending;
            private double _inputAccumulator;
            private double _snapshotAccumulator;

            public override void _Ready()
            {
                _nameField = GetNode<LineEdit>("Ui/LoginPanel/VBox/Name");
                _connectButton = GetNode<Button>("Ui/LoginPanel/VBox/Play");
                _statusLabel = GetNode<Label>("Ui/LoginPanel/VBox/Status");
                _loginPanel = GetNode<Control>("Ui/LoginPanel");
                _hud = GetNode<Control>("Ui/Hud");
                _playerLabel = GetNode<Label>("Ui/Hud/HBox/Player");
                _scoreLabel = GetNode<Label>("Ui/Hud/HBox/Score");
                _healthLabel = GetNode<Label>("Ui/Hud/HBox/Health");
                _connectButton.Pressed += OnConnectPressed;
                _nameField.TextSubmitted += _ => OnConnectPressed();
                ShowLogin("Enter a name to join.");
            }

            public override void _Process(double delta)
            {
                if (_client is null) return;
                while (_client.TryDequeueSnapshot(out var snapshot)) { _world = snapshot; QueueRedraw(); }
                if (_client.ConsumeDisconnected())
                {
                    ShowLogin("Disconnected. Re-enter your name to reconnect.");
                    _ = DisposeClientAsync();
                    return;
                }

                RefreshHud();
                _inputAccumulator += delta;
                _snapshotAccumulator += delta;
                if (_snapshotAccumulator >= 0.1 && !_snapshotPending)
                {
                    _snapshotAccumulator = 0;
                    _ = RefreshWorldAsync();
                }
                if (_localPlayerId == 0 || _inputAccumulator < 0.05 || _inputPending) return;
                _inputAccumulator = 0;
                var direction = Vector2.Zero;
                if (Input.IsKeyPressed(Key.A)) direction.X -= 1f;
                if (Input.IsKeyPressed(Key.D)) direction.X += 1f;
                if (Input.IsKeyPressed(Key.W)) direction.Y += 1f;
                if (Input.IsKeyPressed(Key.S)) direction.Y -= 1f;
                if (direction.LengthSquared() > 1f) direction = direction.Normalized();
                _ = SendInputAsync(direction);
            }

            public override void _Draw()
            {
                if (_world is null || _localPlayerId == 0) return;
                var size = GetViewportRect().Size;
                var arena = new Rect2(24f, 78f, MathF.Max(1f, size.X - 48f), MathF.Max(1f, size.Y - 102f));
                DrawRect(arena, new Color("0d121a"));
                for (var i = 1; i < 16; i++) DrawLine(new Vector2(arena.Position.X + arena.Size.X * i / 16f, arena.Position.Y), new Vector2(arena.Position.X + arena.Size.X * i / 16f, arena.End.Y), new Color(1f, 1f, 1f, 0.05f));
                for (var i = 1; i < 9; i++) DrawLine(new Vector2(arena.Position.X, arena.Position.Y + arena.Size.Y * i / 9f), new Vector2(arena.End.X, arena.Position.Y + arena.Size.Y * i / 9f), new Color(1f, 1f, 1f, 0.05f));

                foreach (var bullet in _world.Bullets) DrawCircle(WorldToScreen(arena, bullet.X, bullet.Y), 5f, Colors.White);
                foreach (var monster in _world.Monsters) DrawCircle(WorldToScreen(arena, monster.X, monster.Y), 12f, new Color("33e64d"));
                foreach (var player in _world.Players)
                {
                    var point = WorldToScreen(arena, player.X, player.Y);
                    var color = PlayerColor(player.PlayerId);
                    if (player.PlayerId == _localPlayerId) DrawCircle(point, 18f, Colors.White);
                    DrawCircle(point, player.IsAlive ? 14f : 9f, player.IsAlive ? color : new Color(color, 0.35f));
                    DrawLine(point, point + new Vector2(player.DirectionX, -player.DirectionY) * 21f, Colors.White, 3f);
                    DrawRect(new Rect2(point.X - 16f, point.Y - 24f, 32f, 4f), new Color("661010"));
                    DrawRect(new Rect2(point.X - 16f, point.Y - 24f, 32f * player.Health / Math.Max(1f, player.MaxHealth), 4f), new Color("33e64d"));
                }
            }

            private async void OnConnectPressed()
            {
                if (_loginPending) return;
                var name = _nameField.Text.Trim();
                if (name.Length is < 1 or > 20) { _statusLabel.Text = "Name must contain 1 to 20 characters."; return; }
                _loginPending = true;
                _connectButton.Disabled = true;
                _connectButton.Text = "CONNECTING...";
                _statusLabel.Text = "Connecting...";
                _cts = new CancellationTokenSource();
                var client = new GameClient(CreateLakonaGameClientOptions());
                try
                {
                    await client.ConnectAsync(_cts.Token);
                    var reply = await client.LoginAsync(name);
                    if (!reply.Success) { _statusLabel.Text = reply.Error; await client.DisposeAsync(); return; }
                    _client = client;
                    _localPlayerId = reply.PlayerId;
                    _world = reply.World;
                    _loginPanel.Visible = false;
                    _hud.Visible = true;
                    QueueRedraw();
                }
                catch (Exception ex)
                {
                    _statusLabel.Text = $"Connection failed: {ex.Message}";
                    await client.DisposeAsync();
                }
                finally
                {
                    _loginPending = false;
                    _connectButton.Disabled = false;
                    _connectButton.Text = "PLAY";
                }
            }

            private async Task SendInputAsync(Vector2 direction)
            {
                if (_client is null) return;
                _inputPending = true;
                try { await _client.SubmitInputAsync(direction.X, direction.Y); }
                catch (Exception) { ShowLogin("Connection lost. Re-enter your name to reconnect."); await DisposeClientAsync(); }
                finally { _inputPending = false; }
            }

            private async Task RefreshWorldAsync()
            {
                if (_client is null) return;
                _snapshotPending = true;
                try { await _client.RefreshWorldAsync(); }
                catch (Exception) { ShowLogin("Connection lost. Re-enter your name to reconnect."); await DisposeClientAsync(); }
                finally { _snapshotPending = false; }
            }

            private void RefreshHud()
            {
                var player = _world?.Players.Find(value => value.PlayerId == _localPlayerId);
                if (player is null) return;
                _playerLabel.Text = $"{player.Name}  #{player.PlayerId}";
                _scoreLabel.Text = $"Score {player.Score}";
                _healthLabel.Text = player.IsAlive ? $"HP {player.Health}/{player.MaxHealth}" : $"Respawn in {player.RespawnSeconds:0.0}s";
            }

            private void ShowLogin(string status)
            {
                _localPlayerId = 0;
                _world = null;
                if (IsInstanceValid(_loginPanel)) _loginPanel.Visible = true;
                if (IsInstanceValid(_hud)) _hud.Visible = false;
                if (IsInstanceValid(_statusLabel)) _statusLabel.Text = status;
                QueueRedraw();
            }

            private Vector2 WorldToScreen(Rect2 arena, float x, float y) => new(arena.Position.X + x / _world!.Width * arena.Size.X, arena.Position.Y + (_world.Height - y) / _world.Height * arena.Size.Y);

            private static Color PlayerColor(long playerId)
            {
                var palette = new[] { new Color("4287f5"), new Color("ef5350"), new Color("ffd54f"), new Color("ab47bc"), new Color("ff8a45"), new Color("26c6da"), new Color("ec6faf") };
                unchecked
                {
                    uint hash = 2166136261;
                    foreach (var ch in playerId.ToString(System.Globalization.CultureInfo.InvariantCulture)) { hash ^= ch; hash *= 16777619; }
                    return palette[(int)(hash % (uint)palette.Length)];
                }
            }

            private LakonaGameClientOptions CreateLakonaGameClientOptions() => new LakonaGameClientOptions({{transportExpression}}, {{serializerExpression}}).UseSecurity(ConfigureTransportSecurity);
            private static string NormalizePath(string path) => string.IsNullOrWhiteSpace(path) ? "" : path.StartsWith("/", StringComparison.Ordinal) ? path : "/" + path;
            private static void ConfigureTransportSecurity(TransportSecurityConfig security) { security.EnableCompression = false; security.EnableEncryption = false; security.EncryptionKeyBase64 = null; }

            private async Task DisposeClientAsync()
            {
                var client = _client; _client = null;
                if (client is not null) await client.DisposeAsync();
            }

            public override void _ExitTree()
            {
                _cts?.Cancel(); _cts?.Dispose(); _ = DisposeClientAsync();
            }
        }
        """;
    }

    public static string RenderLoginClient()
    {
        return """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Client.Generated;
        using Shared.Contracts.Chat;
        using Lakona.Game.Client;
        using Lakona.Rpc.Client;

        namespace Client.Login
        {
            public sealed class LoginClient : ILoginCallback, IChatCallback, IAsyncDisposable
            {
                private readonly LakonaGameClient _gameClient;
                private ILoginService? _loginService;
                private bool _isConnected;

                public event Action<ChatMember>? OnUserJoined;
                public event Action<string>? OnUserLeft;
                public event Action? OnDisconnected;
                public event Action<ChatMessage>? OnMessageReceived;

                public bool IsConnected => _isConnected;
                public LakonaGameClient GameClient => _gameClient;

                public LoginClient(LakonaGameClientOptions options)
                {
                    _gameClient = new LakonaGameClient(options, this);
                    _gameClient.Disconnected += _ =>
                    {
                        _isConnected = false;
                        OnDisconnected?.Invoke();
                    };
                }

                public async Task ConnectAsync(CancellationToken cancellationToken = default)
                {
                    await _gameClient.ConnectAsync(cancellationToken);
                    _loginService = _gameClient.Api.Shared.Login;
                    _isConnected = true;
                }

                public async Task<LoginReply> LoginAsync(string playerName)
                {
                    if (_loginService == null)
                    {
                        throw new InvalidOperationException("Not connected.");
                    }

                    return await _loginService.LoginAsync(new LoginRequest { PlayerName = playerName });
                }

                public async ValueTask DisposeAsync()
                {
                    _isConnected = false;
                    await _gameClient.DisposeAsync();
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

    public static string RenderChatClient()
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
                    _chatService = loginClient.GameClient.Api.Shared.Chat;
                }

                public async Task BindAsync(LoginReply reply)
                {
                    _ = reply;
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

    public static string RenderLoginScene(LakonaProjectSpec spec)
    {
        var transportUsing = RenderTransportUsing(spec.Transport);
        var serializerUsing = RenderSerializerUsing(spec.Serializer);
        var transportExpression = RenderTransportExpression(spec.Transport);
        var serializerExpression = RenderSerializerExpression(spec.Serializer);
        var defaultPath = spec.Transport == TransportKind.WebSocket ? "/ws" : string.Empty;

        return $$"""
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Godot;
        using Shared.Contracts.Chat;
        using Client.Chat;
        using Lakona.Game.Client;
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
                [Export] private string _serverPath = "{{defaultPath}}";

                private readonly CancellationTokenSource _cts = new();
                private LineEdit? _nameField;
                private Button? _connectButton;
                private Label? _statusLabel;
                private bool _isConnecting;

                public override void _Ready()
                {
                    _nameField = GetNode<LineEdit>("%NameField");
                    _nameField.TextSubmitted += _ => OnConnectPressed();

                    _connectButton = GetNode<Button>("%ConnectButton");
                    _connectButton.Pressed += OnConnectPressed;

                    _statusLabel = GetNode<Label>("%StatusLabel");

                    SetBusy(false);

                    if (IsHeadlessSmokeEnabled())
                    {
                        _ = RunHeadlessSmokeAsync();
                    }
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

                    var client = new LoginClient(CreateLakonaGameClientOptions());
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

                private async Task RunHeadlessSmokeAsync()
                {
                    var name = System.Environment.GetEnvironmentVariable("LAKONA_GODOT_SMOKE_NAME");
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        name = "godot-smoke";
                    }

                    var client = new LoginClient(CreateLakonaGameClientOptions());
                    try
                    {
                        await client.ConnectAsync(_cts.Token);
                        var reply = await client.LoginAsync(name);
                        GD.Print($"Ping ok: {reply.Members.Count} online.");
                        await client.DisposeAsync();
                        GetTree().Quit(0);
                    }
                    catch (Exception ex)
                    {
                        GD.PrintErr($"Connect failed: {ex.Message}");
                        await client.DisposeAsync();
                        GetTree().Quit(1);
                    }
                }

                private static bool IsHeadlessSmokeEnabled()
                {
                    var value = System.Environment.GetEnvironmentVariable("LAKONA_GODOT_SMOKE");
                    return string.Equals(value, "1", StringComparison.Ordinal)
                        || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                }

                private LakonaGameClientOptions CreateLakonaGameClientOptions()
                {
                    return new LakonaGameClientOptions(
                        {{transportExpression}},
                        {{serializerExpression}})
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

    public static string RenderChatScene()
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

                    if (loginClient == null || loginReply == null)
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

                    AppendSystemMessage($"Connected. {loginReply.Members.Count} online.");
                    SetOnlineCount(loginReply.Members.Count);

                    foreach (var msg in loginReply.RecentMessages)
                    {
                        AppendMessageText(msg.SenderName, msg.Text);
                    }

                    SetSendBusy(true);
                    _ = BindChatAsync(loginReply);
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

                    if (_client == null)
                    {
                        AppendSystemMessage("Chat not ready.");
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

                private async Task BindChatAsync(LoginReply loginReply)
                {
                    if (_client == null)
                    {
                        return;
                    }

                    try
                    {
                        await _client.BindAsync(loginReply);
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

    private static string RenderTransportUsing(TransportKind transport) => transport switch
    {
        TransportKind.Tcp => "using Lakona.Rpc.Transport.Tcp;",
        TransportKind.WebSocket => "using Lakona.Rpc.Transport.WebSocket;",
        TransportKind.Kcp => "using Lakona.Rpc.Transport.Kcp;",
        _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, null)
    };

    private static string RenderSerializerUsing(SerializerKind serializer) => serializer switch
    {
        SerializerKind.Json => "using Lakona.Rpc.Serializer.Json;",
        SerializerKind.MemoryPack => "using Lakona.Rpc.Serializer.MemoryPack;",
        _ => throw new ArgumentOutOfRangeException(nameof(serializer), serializer, null)
    };

    private static string RenderTransportExpression(TransportKind transport) => transport switch
    {
        TransportKind.Tcp => "new TcpTransport(_serverHost, _serverPort)",
        TransportKind.WebSocket => "new WsTransport($\"ws://{_serverHost}:{_serverPort}{NormalizePath(_serverPath)}\")",
        TransportKind.Kcp => "new KcpTransport(_serverHost, _serverPort)",
        _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, null)
    };

    private static string RenderSerializerExpression(SerializerKind serializer) => serializer switch
    {
        SerializerKind.Json => "new JsonRpcSerializer()",
        SerializerKind.MemoryPack => "new MemoryPackRpcSerializer()",
        _ => throw new ArgumentOutOfRangeException(nameof(serializer), serializer, null)
    };
}
