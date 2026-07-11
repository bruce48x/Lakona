using Lakona.Tool.Domain;

namespace Lakona.Tool.Rendering.Client;

internal static class UnityClientCodeTemplates
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

        namespace Client.Game
        {
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

                public async Task ConnectAsync(CancellationToken cancellationToken)
                {
                    await _client.ConnectAsync(cancellationToken);
                }

                public async Task<LoginReply> LoginAsync(string playerName)
                {
                    return await _client.Api.Shared.Game.LoginAsync(new LoginRequest { PlayerName = playerName });
                }

                public async ValueTask SubmitInputAsync(float x, float y)
                {
                    await _client.Api.Shared.Game.SubmitInputAsync(new PlayerInput { DirectionX = x, DirectionY = y });
                }

                public async ValueTask RefreshWorldAsync()
                {
                    _snapshots.Enqueue(await _client.Api.Shared.Game.GetWorldAsync(new WorldQuery()));
                }

                public bool TryDequeueSnapshot(out WorldSnapshot snapshot)
                {
                    return _snapshots.TryDequeue(out snapshot!);
                }

                public bool ConsumeDisconnected()
                {
                    return Interlocked.Exchange(ref _disconnected, 0) != 0;
                }

                public ValueTask DisposeAsync()
                {
                    return _client.DisposeAsync();
                }

                void IGameCallback.OnWorldUpdated(WorldSnapshot snapshot)
                {
                    _snapshots.Enqueue(snapshot);
                }
            }
        }
        """;
    }

    public static string RenderGameController(LakonaProjectSpec spec)
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
        using Lakona.Game.Client;
        using Lakona.Rpc.Client;
        using Lakona.Rpc.Core;
        using Shared.Contracts.Game;
        {{serializerUsing}}
        {{transportUsing}}
        using UnityEngine;
        using UnityEngine.UIElements;

        namespace Client.Game
        {
            [RequireComponent(typeof(UIDocument))]
            public sealed class GameController : MonoBehaviour
            {
                [SerializeField] private string _serverHost = "127.0.0.1";
                [SerializeField] private int _serverPort = 20000;
                [SerializeField] private string _serverPath = "{{defaultPath}}";

                private CancellationTokenSource? _cts;
                private GameClient? _client;
                private WorldSnapshot? _world;
                private long _localPlayerId;
                private TextField? _nameField;
                private Button? _connectButton;
                private Label? _statusLabel;
                private VisualElement? _loginPanel;
                private VisualElement? _hud;
                private Label? _playerLabel;
                private Label? _scoreLabel;
                private Label? _healthLabel;
                private Texture2D? _circleTexture;
                private bool _loginPending;
                private bool _inputPending;
                private bool _snapshotPending;
                private float _nextInputAt;
                private float _nextSnapshotAt;

                private void Start()
                {
                    var root = GetComponent<UIDocument>().rootVisualElement;
                    _nameField = root.Q<TextField>("name-field");
                    _connectButton = root.Q<Button>("connect-button");
                    _statusLabel = root.Q<Label>("status-label");
                    _loginPanel = root.Q<VisualElement>("login-panel");
                    _hud = root.Q<VisualElement>("hud");
                    _playerLabel = root.Q<Label>("player-label");
                    _scoreLabel = root.Q<Label>("score-label");
                    _healthLabel = root.Q<Label>("health-label");
                    if (_connectButton != null) _connectButton.clicked += OnConnectClicked;
                    _nameField?.RegisterCallback<KeyDownEvent>(evt =>
                    {
                        if (evt.keyCode is KeyCode.Return or KeyCode.KeypadEnter) OnConnectClicked();
                    });
                    ShowLogin("Enter a name to join.");
                    _circleTexture = CreateCircleTexture(64);
                }

                private void Update()
                {
                    if (_client is null) return;
                    while (_client.TryDequeueSnapshot(out var snapshot)) _world = snapshot;
                    if (_client.ConsumeDisconnected())
                    {
                        ShowLogin("Disconnected. Re-enter your name to reconnect.");
                        _ = DisposeClientAsync();
                        return;
                    }

                    RefreshHud();
                    if (Time.unscaledTime >= _nextSnapshotAt && !_snapshotPending)
                    {
                        _nextSnapshotAt = Time.unscaledTime + 0.1f;
                        _ = RefreshWorldAsync();
                    }
                    if (_localPlayerId == 0 || Time.unscaledTime < _nextInputAt || _inputPending) return;
                    _nextInputAt = Time.unscaledTime + 0.05f;
                    var x = Input.GetAxisRaw("Horizontal");
                    var y = Input.GetAxisRaw("Vertical");
                    var length = Mathf.Sqrt(x * x + y * y);
                    if (length > 1f) { x /= length; y /= length; }
                    _ = SendInputAsync(x, y);
                }

                private async void OnConnectClicked()
                {
                    if (_loginPending) return;
                    var name = _nameField?.value?.Trim() ?? "";
                    if (name.Length is < 1 or > 20) { SetStatus("Name must contain 1 to 20 characters."); return; }

                    _loginPending = true;
                    if (_connectButton != null) { _connectButton.SetEnabled(false); _connectButton.text = "CONNECTING..."; }
                    SetStatus("Connecting...");
                    _cts = new CancellationTokenSource();
                    var client = new GameClient(CreateLakonaGameClientOptions());
                    try
                    {
                        await client.ConnectAsync(_cts.Token);
                        var reply = await client.LoginAsync(name);
                        if (!reply.Success)
                        {
                            SetStatus(reply.Error);
                            await client.DisposeAsync();
                            return;
                        }

                        _client = client;
                        _localPlayerId = reply.PlayerId;
                        _world = reply.World;
                        ShowGame();
                    }
                    catch (Exception ex)
                    {
                        SetStatus($"Connection failed: {ex.Message}");
                        await client.DisposeAsync();
                    }
                    finally
                    {
                        _loginPending = false;
                        if (_connectButton != null) { _connectButton.SetEnabled(true); _connectButton.text = "PLAY"; }
                    }
                }

                private async Task SendInputAsync(float x, float y)
                {
                    if (_client is null) return;
                    _inputPending = true;
                    try { await _client.SubmitInputAsync(x, y); }
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

                private void OnGUI()
                {
                    if (_world is null || _localPlayerId == 0 || _circleTexture is null) return;
                    var arena = new Rect(24f, 78f, Screen.width - 48f, Screen.height - 102f);
                    DrawRect(arena, new Color(0.05f, 0.07f, 0.1f, 1f));
                    for (var i = 1; i < 16; i++) DrawRect(new Rect(arena.x + arena.width * i / 16f, arena.y, 1f, arena.height), new Color(1f, 1f, 1f, 0.05f));
                    for (var i = 1; i < 9; i++) DrawRect(new Rect(arena.x, arena.y + arena.height * i / 9f, arena.width, 1f), new Color(1f, 1f, 1f, 0.05f));

                    foreach (var bullet in _world.Bullets) DrawCircle(WorldToScreen(arena, bullet.X, bullet.Y, _world), 5f, Color.white);
                    foreach (var monster in _world.Monsters) DrawCircle(WorldToScreen(arena, monster.X, monster.Y, _world), 12f, new Color(0.2f, 0.9f, 0.3f));
                    foreach (var player in _world.Players)
                    {
                        var point = WorldToScreen(arena, player.X, player.Y, _world);
                        var color = PlayerColor(player.PlayerId);
                        if (player.PlayerId == _localPlayerId) DrawCircle(point, 18f, Color.white);
                        DrawCircle(point, player.IsAlive ? 14f : 9f, player.IsAlive ? color : new Color(color.r, color.g, color.b, 0.35f));
                        DrawLine(point, point + new Vector2(player.DirectionX, -player.DirectionY) * 21f, Color.white, 3f);
                        DrawRect(new Rect(point.x - 16f, point.y - 24f, 32f, 4f), new Color(0.4f, 0.05f, 0.05f));
                        DrawRect(new Rect(point.x - 16f, point.y - 24f, 32f * player.Health / Mathf.Max(1f, player.MaxHealth), 4f), new Color(0.2f, 0.9f, 0.3f));
                    }
                }

                private void RefreshHud()
                {
                    var local = _world?.Players.Find(player => player.PlayerId == _localPlayerId);
                    if (local is null) return;
                    if (_playerLabel != null) _playerLabel.text = $"{local.Name}  #{local.PlayerId}";
                    if (_scoreLabel != null) _scoreLabel.text = $"Score {local.Score}";
                    if (_healthLabel != null) _healthLabel.text = local.IsAlive ? $"HP {local.Health}/{local.MaxHealth}" : $"Respawn in {local.RespawnSeconds:0.0}s";
                }

                private void ShowLogin(string status)
                {
                    _localPlayerId = 0;
                    _world = null;
                    if (_loginPanel != null) _loginPanel.style.display = DisplayStyle.Flex;
                    if (_hud != null) _hud.style.display = DisplayStyle.None;
                    SetStatus(status);
                }

                private void ShowGame()
                {
                    if (_loginPanel != null) _loginPanel.style.display = DisplayStyle.None;
                    if (_hud != null) _hud.style.display = DisplayStyle.Flex;
                }

                private void SetStatus(string text) { if (_statusLabel != null) _statusLabel.text = text; }

                private LakonaGameClientOptions CreateLakonaGameClientOptions()
                {
                    return new LakonaGameClientOptions({{transportExpression}}, {{serializerExpression}}).UseSecurity(ConfigureTransportSecurity);
                }

                private static string NormalizePath(string path) => string.IsNullOrWhiteSpace(path) ? "" : path.StartsWith("/", StringComparison.Ordinal) ? path : "/" + path;
                private static void ConfigureTransportSecurity(TransportSecurityConfig security)
                {
                    security.EnableCompression = false;
                    security.EnableEncryption = false;
                    security.EncryptionKeyBase64 = null;
                }

                private static Vector2 WorldToScreen(Rect arena, float x, float y, WorldSnapshot world) =>
                    new(arena.x + x / world.Width * arena.width, arena.y + (world.Height - y) / world.Height * arena.height);

                private static Color PlayerColor(long playerId)
                {
                    var palette = new[]
                    {
                        new Color(0.26f, 0.53f, 0.96f),
                        new Color(0.94f, 0.33f, 0.31f),
                        new Color(1f, 0.84f, 0.31f),
                        new Color(0.67f, 0.28f, 0.74f),
                        new Color(1f, 0.54f, 0.27f),
                        new Color(0.15f, 0.78f, 0.85f),
                        new Color(0.93f, 0.44f, 0.69f)
                    };
                    unchecked
                    {
                        uint hash = 2166136261;
                        foreach (var ch in playerId.ToString(System.Globalization.CultureInfo.InvariantCulture)) { hash ^= ch; hash *= 16777619; }
                        return palette[(int)(hash % (uint)palette.Length)];
                    }
                }

                private static Texture2D CreateCircleTexture(int size)
                {
                    var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                    var pixels = new Color[size * size];
                    var center = (size - 1) * 0.5f;
                    for (var y = 0; y < size; y++) for (var x = 0; x < size; x++)
                        pixels[y * size + x] = (x - center) * (x - center) + (y - center) * (y - center) <= center * center ? Color.white : Color.clear;
                    texture.SetPixels(pixels); texture.Apply(); return texture;
                }

                private void DrawCircle(Vector2 center, float radius, Color color)
                {
                    var old = GUI.color; GUI.color = color; GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f), _circleTexture); GUI.color = old;
                }
                private static void DrawRect(Rect rect, Color color) { var old = GUI.color; GUI.color = color; GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = old; }
                private static void DrawLine(Vector2 from, Vector2 to, Color color, float width)
                {
                    var oldMatrix = GUI.matrix; var old = GUI.color; GUI.color = color;
                    var angle = Vector2.SignedAngle(Vector2.right, to - from); GUIUtility.RotateAroundPivot(angle, from);
                    GUI.DrawTexture(new Rect(from.x, from.y - width * 0.5f, Vector2.Distance(from, to), width), Texture2D.whiteTexture);
                    GUI.matrix = oldMatrix; GUI.color = old;
                }

                private async Task DisposeClientAsync()
                {
                    var client = _client; _client = null;
                    if (client != null) await client.DisposeAsync();
                }

                private void OnDestroy()
                {
                    _cts?.Cancel(); _cts?.Dispose();
                    if (_circleTexture != null) Destroy(_circleTexture);
                    _ = DisposeClientAsync();
                }
            }
        }
        """;
    }

    public static string RenderDefaultSceneLoader()
    {
        return """
        using UnityEditor;
        using UnityEditor.SceneManagement;
        using UnityEngine;

        [InitializeOnLoad]
        public static class DefaultSceneLoader
        {
            private const string TargetScenePath = "Assets/Scenes/Game.unity";

            static DefaultSceneLoader()
            {
                EditorApplication.delayCall += EnsureDefaultScene;
            }

            private static void EnsureDefaultScene()
            {
                if (Application.isBatchMode)
                {
                    return;
                }

                var initKey = $"Lakona.GameScene.Initialized:{Application.dataPath}";
                if (EditorPrefs.HasKey(initKey))
                {
                    return;
                }

                var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(TargetScenePath);
                if (sceneAsset == null)
                {
                    Debug.LogWarning($"[Lakona.Tool] Missing default Game scene at path: {TargetScenePath}");
                    return;
                }

                if (EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    return;
                }

                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    return;
                }

                if (EditorSceneManager.GetActiveScene().path != TargetScenePath)
                {
                    EditorSceneManager.OpenScene(TargetScenePath, OpenSceneMode.Single);
                    Debug.Log($"[Lakona.Tool] Opened default Game scene: {TargetScenePath}");
                }

                EditorPrefs.SetBool(initKey, true);
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

    public static string RenderLoginUI(LakonaProjectSpec spec)
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
        using Shared.Contracts.Chat;
        using Client.Chat;
        using Lakona.Game.Client;
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
                [SerializeField] private string _serverPath = "{{defaultPath}}";

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

                    var client = new LoginClient(CreateLakonaGameClientOptions());
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

                private void OnDestroy()
                {
                    _cts?.Cancel();
                    _cts?.Dispose();
                }
            }
        }
        """;
    }

    public static string RenderChatUI()
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
                    root.Q<Label>("chat-empty-state")?.RemoveFromHierarchy();

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

                    if (loginClient == null || loginReply == null)
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

                    AppendSystemMessage($"Connected. {loginReply.Members.Count} online.");
                    SetOnlineCount(loginReply.Members.Count);

                    foreach (var msg in loginReply.RecentMessages)
                    {
                        AppendMessage(msg);
                    }

                    SetSendBusy(true);
                    _ = BindChatAsync(loginReply);
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

                private async Task BindChatAsync(LoginReply loginReply)
                {
                    if (_client == null)
                    {
                        return;
                    }

                    try
                    {
                        await _client.BindAsync(loginReply);
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

    public static string RenderNuGetPackageImportGuard()
    {
        return """
        #if UNITY_EDITOR
        using System;
        using System.Collections.Generic;
        using UnityEditor;
        using UnityEditor.Compilation;

        [InitializeOnLoad]
        internal sealed class LakonaGameNuGetPackageImportGuard : AssetPostprocessor
        {
            private const string PreferredRuntimeTfm = "netstandard2.1";
            private const string FallbackRuntimeTfm = "netstandard2.0";

            private static readonly string[] ForbiddenTfms =
            {
                "net10.0", "net9.0", "net8.0", "net7.0", "net6.0",
                "net472", "net48", "net481"
            };

            private static readonly string[] KnownAnalyzerPackageIds =
            {
                "Lakona.Rpc.Analyzers",
                "MemoryPack.Generator",
                "Microsoft.CodeAnalysis.Common",
                "Microsoft.CodeAnalysis.CSharp"
            };

            static LakonaGameNuGetPackageImportGuard()
            {
                ApplyNuGetPluginPolicy();
            }

            private static void OnPostprocessAllAssets(
                string[] importedAssets,
                string[] deletedAssets,
                string[] movedAssets,
                string[] movedFromAssetPaths)
            {
                var touched = false;
                foreach (var assetPath in importedAssets)
                {
                    touched |= TryApplyPolicy(assetPath);
                }

                foreach (var assetPath in movedAssets)
                {
                    touched |= TryApplyPolicy(assetPath);
                }

                if (touched)
                {
                    CompilationPipeline.RequestScriptCompilation();
                }
            }

            private static void ApplyNuGetPluginPolicy()
            {
                var changed = false;
                AssetDatabase.StartAssetEditing();
                try
                {
                    var pluginGuids = AssetDatabase.FindAssets("t:PluginImporter", new[] { "Assets/Packages" });
                    foreach (var guid in pluginGuids)
                    {
                        var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                        changed |= TryApplyPolicy(assetPath);
                    }
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }

                if (changed)
                {
                    CompilationPipeline.RequestScriptCompilation();
                }
            }

            private static bool TryApplyPolicy(string assetPath)
            {
                var normalizedPath = assetPath.Replace('\\', '/');
                if (!normalizedPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                    normalizedPath.IndexOf("Assets/Packages/", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return false;
                }

                var importer = AssetImporter.GetAtPath(assetPath) as PluginImporter;
                if (importer == null)
                {
                    return false;
                }

                if (IsAnalyzerOrGeneratorPlugin(normalizedPath))
                {
                    return DisableAllPlatforms(importer);
                }

                if (TryGetLibAsset(normalizedPath, out var packageRoot, out var tfm, out var fileName))
                {
                    if (IsForbiddenTfm(tfm))
                    {
                        return DisableAllPlatforms(importer);
                    }

                    if (IsPreferredRuntimeTfm(tfm))
                    {
                        return EnableRuntimePlugin(importer);
                    }

                    if (IsFallbackRuntimeTfm(tfm))
                    {
                        return HasHigherPriorityRuntimeSibling(packageRoot, fileName)
                            ? DisableAllPlatforms(importer)
                            : EnableRuntimePlugin(importer);
                    }
                }

                return false;
            }

            private static bool IsAnalyzerOrGeneratorPlugin(string normalizedPath)
            {
                return normalizedPath.IndexOf("/analyzers/", StringComparison.OrdinalIgnoreCase) >= 0
                    || normalizedPath.IndexOf(".Generator.dll", StringComparison.OrdinalIgnoreCase) >= 0
                    || IsKnownAnalyzerPackage(normalizedPath);
            }

            private static bool IsKnownAnalyzerPackage(string normalizedPath)
            {
                foreach (var packageId in KnownAnalyzerPackageIds)
                {
                    var packageMarker = "Assets/Packages/" + packageId + ".";
                    if (normalizedPath.IndexOf(packageMarker, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }

                return false;
            }

            private static bool TryGetLibAsset(
                string normalizedPath,
                out string packageRoot,
                out string tfm,
                out string fileName)
            {
                const string marker = "/lib/";
                var libIndex = normalizedPath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (libIndex < 0)
                {
                    packageRoot = string.Empty;
                    tfm = string.Empty;
                    fileName = string.Empty;
                    return false;
                }

                var start = libIndex + marker.Length;
                var end = normalizedPath.IndexOf('/', start);
                if (end < 0)
                {
                    packageRoot = string.Empty;
                    tfm = string.Empty;
                    fileName = string.Empty;
                    return false;
                }

                var fileStart = normalizedPath.LastIndexOf('/') + 1;
                if (fileStart <= end)
                {
                    packageRoot = string.Empty;
                    tfm = string.Empty;
                    fileName = string.Empty;
                    return false;
                }

                packageRoot = normalizedPath.Substring(0, libIndex);
                tfm = normalizedPath.Substring(start, end - start);
                fileName = normalizedPath.Substring(fileStart);
                return true;
            }

            private static bool IsPreferredRuntimeTfm(string tfm) =>
                string.Equals(tfm, PreferredRuntimeTfm, StringComparison.OrdinalIgnoreCase);

            private static bool IsFallbackRuntimeTfm(string tfm) =>
                string.Equals(tfm, FallbackRuntimeTfm, StringComparison.OrdinalIgnoreCase);

            private static bool IsForbiddenTfm(string tfm) =>
                Array.Exists(ForbiddenTfms, candidate => string.Equals(candidate, tfm, StringComparison.OrdinalIgnoreCase));

            private static bool HasHigherPriorityRuntimeSibling(string packageRoot, string fileName)
            {
                var preferredPath = packageRoot + "/lib/" + PreferredRuntimeTfm + "/" + fileName;
                return AssetDatabase.LoadMainAssetAtPath(preferredPath) != null;
            }

            private static bool DisableAllPlatforms(PluginImporter importer)
            {
                var changed = false;

                if (importer.GetCompatibleWithAnyPlatform())
                {
                    importer.SetCompatibleWithAnyPlatform(false);
                    changed = true;
                }

                if (importer.GetCompatibleWithEditor())
                {
                    importer.SetCompatibleWithEditor(false);
                    changed = true;
                }

                foreach (var target in EnumerateBuildTargets())
                {
                    if (TryGetCompatibleWithPlatform(importer, target))
                    {
                        changed |= TrySetCompatibleWithPlatform(importer, target, false);
                    }
                }

                if (!changed)
                {
                    return false;
                }

                importer.SaveAndReimport();
                return true;
            }

            private static bool EnableRuntimePlugin(PluginImporter importer)
            {
                if (importer.GetCompatibleWithAnyPlatform())
                {
                    return false;
                }

                importer.SetCompatibleWithAnyPlatform(true);
                importer.SaveAndReimport();
                return true;
            }

            private static IEnumerable<BuildTarget> EnumerateBuildTargets()
            {
                foreach (BuildTarget target in Enum.GetValues(typeof(BuildTarget)))
                {
                    if (target == BuildTarget.NoTarget)
                    {
                        continue;
                    }

                    yield return target;
                }
            }

            private static bool TryGetCompatibleWithPlatform(PluginImporter importer, BuildTarget target)
            {
                try
                {
                    return importer.GetCompatibleWithPlatform(target);
                }
                catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException)
                {
                    return false;
                }
            }

            private static bool TrySetCompatibleWithPlatform(PluginImporter importer, BuildTarget target, bool enabled)
            {
                try
                {
                    importer.SetCompatibleWithPlatform(target, enabled);
                    return true;
                }
                catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException)
                {
                    return false;
                }
            }
        }
        #endif
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
