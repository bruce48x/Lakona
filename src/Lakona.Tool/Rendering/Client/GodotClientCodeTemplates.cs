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
        using System.Collections.Generic;
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
            private ProgressBar _healthBar = null!;
            private CancellationTokenSource? _cts;
            private GameClient? _client;
            private WorldSnapshot? _world;
            private long _localPlayerId;
            private bool _loginPending;
            private bool _inputPending;
            private double _inputAccumulator;
            private readonly List<HitEffect> _hitEffects = new();
            private const double HitEffectDuration = 0.22;

            public override void _Ready()
            {
                _nameField = GetNode<LineEdit>("Ui/LoginPanel/VBox/Action/Name");
                _connectButton = GetNode<Button>("Ui/LoginPanel/VBox/Action/Play");
                _statusLabel = GetNode<Label>("Ui/LoginPanel/VBox/Status");
                _loginPanel = GetNode<Control>("Ui/LoginPanel");
                _hud = GetNode<Control>("Ui/Hud");
                _playerLabel = GetNode<Label>("Ui/Hud/HBox/Player");
                _scoreLabel = GetNode<Label>("Ui/Hud/HBox/Score");
                _healthLabel = GetNode<Label>("Ui/Hud/HBox/HealthBox/Health");
                _healthBar = GetNode<ProgressBar>("Ui/Hud/HBox/HealthBox/HealthBar");
                _connectButton.Pressed += OnConnectPressed;
                _nameField.TextSubmitted += _ => OnConnectPressed();
                ShowLogin("Enter a name to join.");
            }

            public override void _Process(double delta)
            {
                if (_client is null) { QueueRedraw(); return; }
                while (_client.TryDequeueSnapshot(out var snapshot)) ApplyWorldSnapshot(snapshot);
                if (_client.ConsumeDisconnected())
                {
                    ShowLogin("Disconnected. Re-enter your name to reconnect.");
                    _ = DisposeClientAsync();
                    return;
                }

                RefreshHud();
                var now = Time.GetTicksMsec() / 1000.0;
                var hadHitEffects = _hitEffects.Count > 0;
                _hitEffects.RemoveAll(effect => effect.ExpiresAt <= now);
                if (hadHitEffects) QueueRedraw();
                _inputAccumulator += delta;
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
                var size = GetViewportRect().Size;
                var arena = new Rect2(Vector2.Zero, size);
                DrawArenaBackdrop(arena);
                if (_world is null || _localPlayerId == 0)
                {
                    DrawDemoBattle(arena);
                    return;
                }

                foreach (var bullet in _world.Bullets)
                {
                    var point = WorldToScreen(arena, bullet.X, bullet.Y);
                    var direction = new Vector2(bullet.DirectionX, -bullet.DirectionY);
                    if (direction.LengthSquared() < 0.001f) direction = Vector2.Right;
                    direction = direction.Normalized();
                    DrawLine(point - direction * 12f, point + direction * 4f, new Color("bee31c"), 3f);
                }
                foreach (var monster in _world.Monsters)
                {
                    var point = WorldToScreen(arena, monster.X, monster.Y);
                    DrawAgent(point, new Color("ff4c40"), Vector2.Left, 12f);
                    DrawSegmentedHealth(point + new Vector2(0f, -23f), 36f, monster.Health, monster.MaxHealth, new Color("ff4c40"));
                }
                foreach (var player in _world.Players)
                {
                    var point = WorldToScreen(arena, player.X, player.Y);
                    var color = PlayerColor(player.PlayerId);
                    var direction = new Vector2(player.DirectionX, -player.DirectionY);
                    if (player.PlayerId == _localPlayerId) DrawArc(point, 21f, 0f, MathF.Tau, 40, new Color("bee31c"), 3f);
                    DrawAgent(point, player.IsAlive ? color : new Color(color, 0.35f), direction, player.IsAlive ? 14f : 9f);
                    DrawSegmentedHealth(point + new Vector2(0f, -26f), 42f, player.Health, player.MaxHealth, color);
                }

                var now = Time.GetTicksMsec() / 1000.0;
                foreach (var effect in _hitEffects)
                {
                    var remaining = effect.ExpiresAt - now;
                    var progress = 1.0 - remaining / HitEffectDuration;
                    var alpha = (float)Math.Clamp(remaining / HitEffectDuration, 0.0, 1.0);
                    DrawArc(WorldToScreen(arena, effect.X, effect.Y), (float)(16.0 + 14.0 * progress), 0f, MathF.Tau, 40, new Color(1f, 0.8f, 0.12f, alpha), 3f);
                }
            }

            private void DrawArenaBackdrop(Rect2 arena)
            {
                DrawRect(arena, new Color("0c0e0e"));
                const float spacing = 48f;
                for (var x = 0f; x < arena.Size.X; x += spacing) DrawLine(new Vector2(x, 0f), new Vector2(x, arena.Size.Y), new Color(1f, 1f, 1f, 0.035f));
                for (var y = 0f; y < arena.Size.Y; y += spacing) DrawLine(new Vector2(0f, y), new Vector2(arena.Size.X, y), new Color(1f, 1f, 1f, 0.035f));
                var center = arena.GetCenter();
                var radius = MathF.Min(arena.Size.X, arena.Size.Y) * 0.43f;
                DrawArc(center, radius, 0f, MathF.Tau, 96, new Color(1f, 1f, 1f, 0.13f), 8f);
                DrawArc(center, radius * 0.58f, 0f, MathF.Tau, 72, new Color(1f, 1f, 1f, 0.1f), 4f);
                DrawLine(new Vector2(center.X, center.Y - radius), new Vector2(center.X, center.Y - radius * 0.72f), new Color(1f, 1f, 1f, 0.16f), 4f);
                DrawLine(new Vector2(center.X, center.Y + radius * 0.72f), new Vector2(center.X, center.Y + radius), new Color(1f, 1f, 1f, 0.16f), 4f);
                DrawLine(new Vector2(center.X - radius, center.Y), new Vector2(center.X - radius * 0.72f, center.Y), new Color(1f, 1f, 1f, 0.16f), 4f);
                DrawLine(new Vector2(center.X + radius * 0.72f, center.Y), new Vector2(center.X + radius, center.Y), new Color(1f, 1f, 1f, 0.16f), 4f);
            }

            private void DrawDemoBattle(Rect2 arena)
            {
                var lime = new Color("bee31c");
                var coral = new Color("ff4c40");
                DrawAgent(new Vector2(arena.Size.X * 0.18f, arena.Size.Y * 0.2f), lime, new Vector2(0.9f, 0.35f), 17f);
                DrawAgent(new Vector2(arena.Size.X * 0.13f, arena.Size.Y * 0.62f), coral, new Vector2(0.95f, 0.2f), 15f);
                DrawAgent(new Vector2(arena.Size.X * 0.84f, arena.Size.Y * 0.16f), coral, new Vector2(-0.9f, 0.35f), 17f);
                DrawAgent(new Vector2(arena.Size.X * 0.88f, arena.Size.Y * 0.58f), lime, new Vector2(-0.9f, -0.2f), 15f);
                DrawSegmentedHealth(new Vector2(arena.Size.X * 0.18f, arena.Size.Y * 0.2f - 30f), 48f, 4, 5, lime);
                DrawSegmentedHealth(new Vector2(arena.Size.X * 0.84f, arena.Size.Y * 0.16f - 30f), 48f, 3, 5, coral);
                DrawLine(new Vector2(arena.Size.X * 0.21f, arena.Size.Y * 0.22f), new Vector2(arena.Size.X * 0.29f, arena.Size.Y * 0.27f), lime, 4f);
                DrawLine(new Vector2(arena.Size.X * 0.79f, arena.Size.Y * 0.2f), new Vector2(arena.Size.X * 0.72f, arena.Size.Y * 0.27f), coral, 4f);
            }

            private void DrawAgent(Vector2 point, Color color, Vector2 direction, float radius)
            {
                DrawArc(point, radius + 6f, 0f, MathF.Tau, 36, color, 3f);
                DrawCircle(point, radius, color);
                if (direction.LengthSquared() > 0.001f) DrawLine(point, point + direction.Normalized() * (radius + 18f), color, 4f);
            }

            private void DrawSegmentedHealth(Vector2 center, float width, int health, int maxHealth, Color color)
            {
                const int segments = 5;
                const float gap = 2f;
                var segmentWidth = (width - gap * (segments - 1)) / segments;
                var ratio = health / Math.Max(1f, maxHealth);
                for (var index = 0; index < segments; index++)
                {
                    var x = center.X - width * 0.5f + index * (segmentWidth + gap);
                    var filled = ratio > index / (float)segments;
                    DrawRect(new Rect2(x, center.Y, segmentWidth, 5f), filled ? color : new Color("383d38"));
                }
            }

            private void ApplyWorldSnapshot(WorldSnapshot snapshot)
            {
                if (_world is not null && snapshot.Tick <= _world.Tick) return;
                if (_world is not null)
                {
                    foreach (var monster in snapshot.Monsters)
                    {
                        var previous = _world.Monsters.Find(value => value.MonsterId == monster.MonsterId);
                        if (previous is not null && monster.Health < previous.Health) AddHitEffect(monster.X, monster.Y);
                    }
                    foreach (var previous in _world.Monsters)
                    {
                        if (snapshot.Monsters.Find(value => value.MonsterId == previous.MonsterId) is null) AddHitEffect(previous.X, previous.Y);
                    }
                    foreach (var player in snapshot.Players)
                    {
                        var previous = _world.Players.Find(value => value.PlayerId == player.PlayerId);
                        if (previous is not null && player.Health < previous.Health) AddHitEffect(player.X, player.Y);
                    }
                }
                _world = snapshot;
                QueueRedraw();
            }

            private void AddHitEffect(float x, float y) => _hitEffects.Add(new HitEffect(x, y, Time.GetTicksMsec() / 1000.0 + HitEffectDuration));

            private sealed class HitEffect
            {
                public HitEffect(float x, float y, double expiresAt) { X = x; Y = y; ExpiresAt = expiresAt; }
                public float X { get; }
                public float Y { get; }
                public double ExpiresAt { get; }
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
                    _hitEffects.Clear();
                    _loginPanel.Visible = false;
                    _hud.Visible = GetViewportRect().Size.Y >= 600.0f;
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
                    _connectButton.Text = "PLAY NOW";
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

            private void RefreshHud()
            {
                var player = _world?.Players.Find(value => value.PlayerId == _localPlayerId);
                if (player is null) return;
                _playerLabel.Text = $"{player.Name.ToUpperInvariant()}  #{player.PlayerId:00}";
                _scoreLabel.Text = $"SCORE {player.Score}";
                _healthLabel.Text = player.IsAlive ? $"HEALTH {player.Health} / {player.MaxHealth}" : $"RESPAWN {player.RespawnSeconds:0.0}s";
                _healthBar.Value = player.IsAlive ? 100.0 * player.Health / Math.Max(1, player.MaxHealth) : 0.0;
            }

            private void ShowLogin(string status)
            {
                _localPlayerId = 0;
                _world = null;
                _hitEffects.Clear();
                if (IsInstanceValid(_loginPanel)) _loginPanel.Visible = true;
                if (IsInstanceValid(_hud))
                {
                    _hud.Visible = true;
                    _playerLabel.Text = "LAKONA_01";
                    _scoreLabel.Text = "SCORE 12,540";
                    _healthLabel.Text = "HEALTH 100 / 100";
                    _healthBar.Value = 100.0;
                }
                if (IsInstanceValid(_statusLabel)) _statusLabel.Text = status;
                QueueRedraw();
            }

            private Vector2 CameraCenter(Rect2 arena)
            {
                var local = _world!.Players.Find(player => player.PlayerId == _localPlayerId);
                if (local is null) return new Vector2(_world.Width * 0.5f, _world.Height * 0.5f);
                var visibleHeight = MathF.Min(_world.Height, 12f);
                var visibleWidth = MathF.Min(_world.Width, visibleHeight * arena.Size.X / MathF.Max(1f, arena.Size.Y));
                var centerX = visibleWidth >= _world.Width ? _world.Width * 0.5f : Math.Clamp(local.X, visibleWidth * 0.5f, _world.Width - visibleWidth * 0.5f);
                var centerY = visibleHeight >= _world.Height ? _world.Height * 0.5f : Math.Clamp(local.Y, visibleHeight * 0.5f, _world.Height - visibleHeight * 0.5f);
                return new Vector2(centerX, centerY);
            }

            private Vector2 WorldToScreen(Rect2 arena, float x, float y)
            {
                var visibleHeight = MathF.Min(_world!.Height, 12f);
                var visibleWidth = MathF.Min(_world.Width, visibleHeight * arena.Size.X / MathF.Max(1f, arena.Size.Y));
                var scale = MathF.Min(arena.Size.X / visibleWidth, arena.Size.Y / visibleHeight);
                var camera = CameraCenter(arena);
                return arena.GetCenter() + new Vector2((x - camera.X) * scale, -(y - camera.Y) * scale);
            }

            private static Color PlayerColor(long playerId)
            {
                var palette = new[] { new Color("bee31c"), new Color("45a7ff"), new Color("f4f1e2"), new Color("9b7cff"), new Color("35d0ba"), new Color("ffb238"), new Color("e97cb3") };
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
