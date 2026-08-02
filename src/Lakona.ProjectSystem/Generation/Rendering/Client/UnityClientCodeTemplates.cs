using Lakona.ProjectSystem.Generation.Domain;

namespace Lakona.ProjectSystem.Generation.Rendering.Client;

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
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Lakona.Game.Client;
        using Lakona.Rpc.Client;
        using Lakona.Rpc.Core;
        using Shared.Contracts.Game;
        {{serializerUsing}}
        {{transportUsing}}
        using UnityEngine;
        using UnityEngine.InputSystem;
        using UnityEngine.UIElements;

        namespace Client.Game
        {
            [RequireComponent(typeof(UIDocument))]
            public sealed class GameController : MonoBehaviour
            {
                [SerializeField] private string _serverHost = "127.0.0.1";
                [SerializeField] private int _serverPort = 20000;
                [SerializeField] private string _serverPath = "{{defaultPath}}";
                [SerializeField] private InputActionAsset _inputActions = null!;

                private CancellationTokenSource? _cts;
                private InputAction _moveAction = null!;
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
                private VisualElement? _healthFill;
                private VisualElement? _arenaView;
                private VisualElement? _root;
                private bool _loginPending;
                private bool _inputPending;
                private float _nextInputAt;
                private readonly List<HitEffect> _hitEffects = new();
                private const float HitEffectDuration = 0.22f;

                private void OnEnable()
                {
                    _moveAction = _inputActions.FindAction("Player/Move", true);
                    _moveAction.Enable();
                }

                private void OnDisable()
                {
                    _moveAction?.Disable();
                }

                private void Start()
                {
                    var root = GetComponent<UIDocument>().rootVisualElement;
                    _root = root;
                    root.RegisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);
                    _arenaView = root.Q<VisualElement>("arena-view");
                    _nameField = root.Q<TextField>("name-field");
                    _connectButton = root.Q<Button>("connect-button");
                    _statusLabel = root.Q<Label>("status-label");
                    _loginPanel = root.Q<VisualElement>("login-panel");
                    _hud = root.Q<VisualElement>("hud");
                    _playerLabel = root.Q<Label>("player-label");
                    _scoreLabel = root.Q<Label>("score-label");
                    _healthLabel = root.Q<Label>("health-label");
                    _healthFill = root.Q<VisualElement>("health-fill");
                    if (_arenaView != null) _arenaView.generateVisualContent += GenerateArenaVisualContent;
                    if (_connectButton != null) _connectButton.clicked += OnConnectClicked;
                    _nameField?.RegisterCallback<KeyDownEvent>(evt =>
                    {
                        if (evt.keyCode is KeyCode.Return or KeyCode.KeypadEnter) OnConnectClicked();
                    });
                    ShowLogin("Enter a name to join.");
                }

                private void Update()
                {
                    _arenaView?.MarkDirtyRepaint();
                    if (_client is null) return;
                    while (_client.TryDequeueSnapshot(out var snapshot)) ApplyWorldSnapshot(snapshot);
                    if (_client.ConsumeDisconnected())
                    {
                        ShowLogin("Disconnected. Re-enter your name to reconnect.");
                        _ = DisposeClientAsync();
                        return;
                    }

                    RefreshHud();
                    if (_localPlayerId == 0 || Time.unscaledTime < _nextInputAt || _inputPending) return;
                    _nextInputAt = Time.unscaledTime + 0.05f;
                    var direction = _moveAction.ReadValue<Vector2>();
                    var x = direction.x;
                    var y = direction.y;
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
                        _hitEffects.Clear();
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
                        if (_connectButton != null) { _connectButton.SetEnabled(true); _connectButton.text = "PLAY NOW"; }
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

                private void GenerateArenaVisualContent(MeshGenerationContext context)
                {
                    if (_arenaView is null) return;
                    var painter = context.painter2D;
                    var arena = _arenaView.contentRect;
                    if (arena.width <= 1f || arena.height <= 1f) return;
                    DrawArenaBackdrop(painter, arena);
                    if (_world is null || _localPlayerId == 0)
                    {
                        DrawDemoBattle(painter, arena);
                        return;
                    }

                    foreach (var bullet in _world.Bullets)
                    {
                        var point = WorldToScreen(arena, bullet.X, bullet.Y, _world);
                        var direction = new Vector2(bullet.DirectionX, -bullet.DirectionY);
                        if (direction.sqrMagnitude < 0.001f) direction = Vector2.right;
                        direction.Normalize();
                        DrawLine(painter, point - direction * 12f, point + direction * 4f, new Color(0.75f, 0.89f, 0.11f), 3f);
                    }
                    foreach (var monster in _world.Monsters)
                    {
                        var point = WorldToScreen(arena, monster.X, monster.Y, _world);
                        DrawAgent(painter, point, new Color(1f, 0.3f, 0.25f), Vector2.left, 12f);
                        DrawSegmentedHealth(painter, point + new Vector2(0f, -23f), 36f, monster.Health, monster.MaxHealth, new Color(1f, 0.3f, 0.25f));
                    }
                    foreach (var player in _world.Players)
                    {
                        var point = WorldToScreen(arena, player.X, player.Y, _world);
                        var color = PlayerColor(player.PlayerId);
                        var direction = new Vector2(player.DirectionX, -player.DirectionY);
                        if (player.PlayerId == _localPlayerId) DrawRing(painter, point, 21f, new Color(0.75f, 0.89f, 0.11f));
                        DrawAgent(painter, point, player.IsAlive ? color : new Color(color.r, color.g, color.b, 0.35f), direction, player.IsAlive ? 14f : 9f);
                        DrawSegmentedHealth(painter, point + new Vector2(0f, -26f), 42f, player.Health, player.MaxHealth, color);
                    }

                    for (var index = _hitEffects.Count - 1; index >= 0; index--)
                    {
                        var effect = _hitEffects[index];
                        var remaining = effect.ExpiresAt - Time.unscaledTime;
                        if (remaining <= 0f) { _hitEffects.RemoveAt(index); continue; }
                        var progress = 1f - remaining / HitEffectDuration;
                        var alpha = Mathf.Clamp01(remaining / HitEffectDuration);
                        DrawRing(painter, WorldToScreen(arena, effect.X, effect.Y, _world), Mathf.Lerp(16f, 30f, progress), new Color(1f, 0.8f, 0.12f, alpha));
                    }
                }

                private static void DrawArenaBackdrop(Painter2D painter, Rect arena)
                {
                    DrawRect(painter, arena, new Color(0.045f, 0.052f, 0.05f, 1f));
                    const float spacing = 48f;
                    for (var x = 0f; x < arena.width; x += spacing) DrawRect(painter, new Rect(x, 0f, 1f, arena.height), new Color(1f, 1f, 1f, 0.035f));
                    for (var y = 0f; y < arena.height; y += spacing) DrawRect(painter, new Rect(0f, y, arena.width, 1f), new Color(1f, 1f, 1f, 0.035f));
                    var center = arena.center;
                    var radius = Mathf.Min(arena.width, arena.height) * 0.43f;
                    DrawRing(painter, center, radius, new Color(1f, 1f, 1f, 0.12f), 8f);
                    DrawRing(painter, center, radius * 0.58f, new Color(1f, 1f, 1f, 0.1f), 4f);
                    DrawLine(painter, new Vector2(center.x, center.y - radius), new Vector2(center.x, center.y - radius * 0.72f), new Color(1f, 1f, 1f, 0.16f), 4f);
                    DrawLine(painter, new Vector2(center.x, center.y + radius * 0.72f), new Vector2(center.x, center.y + radius), new Color(1f, 1f, 1f, 0.16f), 4f);
                    DrawLine(painter, new Vector2(center.x - radius, center.y), new Vector2(center.x - radius * 0.72f, center.y), new Color(1f, 1f, 1f, 0.16f), 4f);
                    DrawLine(painter, new Vector2(center.x + radius * 0.72f, center.y), new Vector2(center.x + radius, center.y), new Color(1f, 1f, 1f, 0.16f), 4f);
                }

                private static void DrawDemoBattle(Painter2D painter, Rect arena)
                {
                    var lime = new Color(0.75f, 0.89f, 0.11f);
                    var coral = new Color(1f, 0.3f, 0.25f);
                    DrawAgent(painter, new Vector2(arena.width * 0.18f, arena.height * 0.2f), lime, new Vector2(0.9f, 0.35f), 17f);
                    DrawAgent(painter, new Vector2(arena.width * 0.13f, arena.height * 0.62f), coral, new Vector2(0.95f, 0.2f), 15f);
                    DrawAgent(painter, new Vector2(arena.width * 0.84f, arena.height * 0.16f), coral, new Vector2(-0.9f, 0.35f), 17f);
                    DrawAgent(painter, new Vector2(arena.width * 0.88f, arena.height * 0.58f), lime, new Vector2(-0.9f, -0.2f), 15f);
                    DrawSegmentedHealth(painter, new Vector2(arena.width * 0.18f, arena.height * 0.2f - 30f), 48f, 4, 5, lime);
                    DrawSegmentedHealth(painter, new Vector2(arena.width * 0.84f, arena.height * 0.16f - 30f), 48f, 3, 5, coral);
                    DrawLine(painter, new Vector2(arena.width * 0.21f, arena.height * 0.22f), new Vector2(arena.width * 0.29f, arena.height * 0.27f), lime, 4f);
                    DrawLine(painter, new Vector2(arena.width * 0.79f, arena.height * 0.2f), new Vector2(arena.width * 0.72f, arena.height * 0.27f), coral, 4f);
                }

                private static void DrawAgent(Painter2D painter, Vector2 point, Color color, Vector2 direction, float radius)
                {
                    DrawRing(painter, point, radius + 6f, color, 3f);
                    DrawCircle(painter, point, radius, color);
                    if (direction.sqrMagnitude > 0.001f) DrawLine(painter, point, point + direction.normalized * (radius + 18f), color, 4f);
                }

                private static void DrawSegmentedHealth(Painter2D painter, Vector2 center, float width, int health, int maxHealth, Color color)
                {
                    const int segments = 5;
                    const float gap = 2f;
                    var segmentWidth = (width - gap * (segments - 1)) / segments;
                    var ratio = health / Mathf.Max(1f, maxHealth);
                    for (var index = 0; index < segments; index++)
                    {
                        var x = center.x - width * 0.5f + index * (segmentWidth + gap);
                        var filled = ratio > index / (float)segments;
                        DrawRect(painter, new Rect(x, center.y, segmentWidth, 5f), filled ? color : new Color(0.22f, 0.24f, 0.22f));
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
                }

                private void AddHitEffect(float x, float y) => _hitEffects.Add(new HitEffect(x, y, Time.unscaledTime + HitEffectDuration));

                private sealed class HitEffect
                {
                    public HitEffect(float x, float y, float expiresAt) { X = x; Y = y; ExpiresAt = expiresAt; }
                    public float X { get; }
                    public float Y { get; }
                    public float ExpiresAt { get; }
                }

                private void RefreshHud()
                {
                    var local = _world?.Players.Find(player => player.PlayerId == _localPlayerId);
                    if (local is null) return;
                    if (_playerLabel != null) _playerLabel.text = $"{local.Name.ToUpperInvariant()}  #{local.PlayerId:00}";
                    if (_scoreLabel != null) _scoreLabel.text = $"SCORE {local.Score:N0}";
                    if (_healthLabel != null) _healthLabel.text = local.IsAlive ? $"HEALTH {local.Health} / {local.MaxHealth}" : $"RESPAWN {local.RespawnSeconds:0.0}s";
                    if (_healthFill != null) _healthFill.style.width = Length.Percent(local.IsAlive ? 100f * local.Health / Mathf.Max(1f, local.MaxHealth) : 0f);
                }

                private void ShowLogin(string status)
                {
                    _localPlayerId = 0;
                    _world = null;
                    _hitEffects.Clear();
                    if (_loginPanel != null) _loginPanel.style.display = DisplayStyle.Flex;
                    if (_hud != null)
                    {
                        if (_playerLabel != null) _playerLabel.text = "LAKONA_01";
                        if (_scoreLabel != null) _scoreLabel.text = "SCORE 12,540";
                        if (_healthLabel != null) _healthLabel.text = "HEALTH 100 / 100";
                        if (_healthFill != null) _healthFill.style.width = Length.Percent(100f);
                    }
                    UpdateLoginHudVisibility();
                    SetStatus(status);
                }

                private void ShowGame()
                {
                    if (_loginPanel != null) _loginPanel.style.display = DisplayStyle.None;
                    if (_hud != null) _hud.style.display = DisplayStyle.Flex;
                }

                private void OnRootGeometryChanged(GeometryChangedEvent evt)
                {
                    if (_loginPanel?.resolvedStyle.display == DisplayStyle.Flex) UpdateLoginHudVisibility();
                }

                private void UpdateLoginHudVisibility()
                {
                    if (_hud == null || _root == null) return;
                    var compact = _root.resolvedStyle.height < 600f;
                    _root.EnableInClassList("compact", compact);
                    _hud.style.display = compact ? DisplayStyle.None : DisplayStyle.Flex;
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

                private Vector2 CameraCenter(Rect arena, WorldSnapshot world)
                {
                    var local = world.Players.Find(player => player.PlayerId == _localPlayerId);
                    if (local is null) return new Vector2(world.Width * 0.5f, world.Height * 0.5f);
                    var visibleHeight = Mathf.Min(world.Height, 12f);
                    var visibleWidth = Mathf.Min(world.Width, visibleHeight * arena.width / Mathf.Max(1f, arena.height));
                    var centerX = visibleWidth >= world.Width ? world.Width * 0.5f : Mathf.Clamp(local.X, visibleWidth * 0.5f, world.Width - visibleWidth * 0.5f);
                    var centerY = visibleHeight >= world.Height ? world.Height * 0.5f : Mathf.Clamp(local.Y, visibleHeight * 0.5f, world.Height - visibleHeight * 0.5f);
                    return new Vector2(centerX, centerY);
                }

                private Vector2 WorldToScreen(Rect arena, float x, float y, WorldSnapshot world)
                {
                    var visibleHeight = Mathf.Min(world.Height, 12f);
                    var visibleWidth = Mathf.Min(world.Width, visibleHeight * arena.width / Mathf.Max(1f, arena.height));
                    var scale = Mathf.Min(arena.width / visibleWidth, arena.height / visibleHeight);
                    var camera = CameraCenter(arena, world);
                    return arena.center + new Vector2((x - camera.x) * scale, -(y - camera.y) * scale);
                }

                private static Color PlayerColor(long playerId)
                {
                    var palette = new[]
                    {
                        new Color(0.75f, 0.89f, 0.11f),
                        new Color(0.27f, 0.65f, 1f),
                        new Color(0.96f, 0.95f, 0.89f),
                        new Color(0.61f, 0.49f, 1f),
                        new Color(0.21f, 0.82f, 0.73f),
                        new Color(1f, 0.7f, 0.22f),
                        new Color(0.91f, 0.49f, 0.7f)
                    };
                    unchecked
                    {
                        uint hash = 2166136261;
                        foreach (var ch in playerId.ToString(System.Globalization.CultureInfo.InvariantCulture)) { hash ^= ch; hash *= 16777619; }
                        return palette[(int)(hash % (uint)palette.Length)];
                    }
                }

                private static void DrawCircle(Painter2D painter, Vector2 center, float radius, Color color)
                {
                    painter.fillColor = color;
                    painter.BeginPath();
                    painter.Arc(center, radius, 0f, 360f);
                    painter.Fill();
                }

                private static void DrawRing(Painter2D painter, Vector2 center, float radius, Color color, float width = 3f)
                {
                    painter.strokeColor = color;
                    painter.lineWidth = width;
                    painter.BeginPath();
                    painter.Arc(center, radius, 0f, 360f);
                    painter.Stroke();
                }

                private static void DrawRect(Painter2D painter, Rect rect, Color color)
                {
                    painter.fillColor = color;
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(rect.xMin, rect.yMin));
                    painter.LineTo(new Vector2(rect.xMax, rect.yMin));
                    painter.LineTo(new Vector2(rect.xMax, rect.yMax));
                    painter.LineTo(new Vector2(rect.xMin, rect.yMax));
                    painter.ClosePath();
                    painter.Fill();
                }

                private static void DrawLine(Painter2D painter, Vector2 from, Vector2 to, Color color, float width)
                {
                    painter.strokeColor = color;
                    painter.lineWidth = width;
                    painter.BeginPath();
                    painter.MoveTo(from);
                    painter.LineTo(to);
                    painter.Stroke();
                }

                private async Task DisposeClientAsync()
                {
                    var client = _client; _client = null;
                    if (client != null) await client.DisposeAsync();
                }

                private void OnDestroy()
                {
                    _cts?.Cancel(); _cts?.Dispose();
                    if (_arenaView != null) _arenaView.generateVisualContent -= GenerateArenaVisualContent;
                    if (_root != null) _root.UnregisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);
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
