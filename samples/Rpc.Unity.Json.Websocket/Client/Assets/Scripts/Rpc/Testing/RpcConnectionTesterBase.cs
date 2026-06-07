#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Game.Rpc.Contracts;
using Rpc.Generated;
using Lakona.Rpc.Client;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rpc.Testing
{
    public enum RpcTransportKind
    {
        Tcp,
        WebSocket,
        Kcp
    }

    [Serializable]
    public sealed class RpcEndpointSettings
    {
        public string Host = "127.0.0.1";
        public int Port = 20000;
        public string Path = "/ws";

        public static RpcEndpointSettings CreateTcp(string host, int port)
        {
            return new RpcEndpointSettings
            {
                Host = host,
                Port = port
            };
        }

        public static RpcEndpointSettings CreateKcp(string host, int port)
        {
            return new RpcEndpointSettings
            {
                Host = host,
                Port = port
            };
        }

        public static RpcEndpointSettings CreateWebSocket(string host, int port, string path = "/ws")
        {
            return new RpcEndpointSettings
            {
                Host = host,
                Port = port,
                Path = path
            };
        }

        public string GetDisplayAddress(RpcTransportKind transport)
        {
            return transport == RpcTransportKind.WebSocket
                ? GetWebSocketUrl()
                : $"{Host}:{Port}";
        }

        public string GetWebSocketUrl()
        {
            var normalizedPath = string.IsNullOrWhiteSpace(Path)
                ? "/ws"
                : Path.StartsWith("/", StringComparison.Ordinal) ? Path : "/" + Path;
            return $"ws://{Host}:{Port}{normalizedPath}";
        }
    }

    public abstract class RpcConnectionTesterBase : MonoBehaviour
    {
        private const int MaxLogEntries = 24;

        protected abstract RpcEndpointSettings Endpoint { get; }
        protected abstract string RuntimeTitle { get; }
        protected abstract RpcTransportKind TransportKind { get; }
        protected abstract RpcClientOptions CreateClientOptions();

        [Header("Login")] public string Account = "a";
        public string Password = "b";

        [Header("Multi Connection")] public int ConnectionCount = 3;
        public float RequestIntervalSeconds = 1f;

        [Header("Debug UI")] public bool ShowDebugPanel = true;
        public bool AutoConnect = true;
        [SerializeField] private UIDocument? _debugDocument;

        private readonly Dictionary<int, SessionSnapshot> _sessionSnapshots = new();
        private readonly List<ConnectionSession> _sessions = new();
        private readonly List<string> _logEntries = new();
        private ConnectionState _state = ConnectionState.Idle;

        private Label? _titleLabel;
        private Label? _stateLabel;
        private Label? _transportLabel;
        private Label? _endpointLabel;
        private Label? _connectionsLabel;
        private Label? _intervalLabel;
        private Button? _connectButton;
        private ScrollView? _sessionsScroll;
        private ScrollView? _logsScroll;
        private VisualElement? _sessionsContent;
        private Label? _logsLabel;
        private readonly Dictionary<int, SessionView> _sessionViews = new();
        private bool _cleanupStarted;
        private bool _debugUiBound;
        private bool _isShuttingDown;
        private bool _styleSheetAttached;
        private bool _uiDirty = true;
        private bool _uiToolkitReady;

        public event Action<ConnectionState, string?>? StatusChanged;

        public enum ConnectionState
        {
            Idle,
            Connecting,
            Connected,
            Disconnected,
            Error
        }

        private void Awake()
        {
            EnsureDebugUi();
            MarkUiDirty();
        }

        private void OnEnable()
        {
            EnsureDebugUi();
            MarkUiDirty();
        }

        private void Update()
        {
            if (_uiDirty)
                RefreshDebugUi();
        }

        private async void Start()
        {
            if (!AutoConnect)
                return;

            await ConnectAndTestAsync();
        }

        private void OnDisable()
        {
            BeginShutdown();
        }

        private void OnDestroy()
        {
            BeginShutdown();
            TeardownDebugUi();
        }

        private void OnValidate()
        {
            MarkUiDirty();
        }

        public async Task ConnectAndTestAsync()
        {
            if (_isShuttingDown)
                return;

            if (_sessions.Count > 0)
            {
                Debug.LogWarning("RpcConnectionTester already connected.");
                return;
            }

            try
            {
                UpdateStatus(ConnectionState.Connecting, "Connecting...");
                var targetCount = Mathf.Max(1, ConnectionCount);

                for (var i = 0; i < targetCount; i++)
                {
                    var session = new ConnectionSession(this, i);
                    _sessions.Add(session);
                    MarkUiDirty();
                    await session.StartAsync();
                }

                UpdateStatus(ConnectionState.Connected, $"Connected x{_sessions.Count}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"RPC test failed: {ex}");
                UpdateStatus(ConnectionState.Error, ex.Message);
                await CleanupAsync(false);
            }
        }

        public void ConnectFromUi()
        {
            _ = ConnectAndTestAsync();
        }

        protected void UpdateSession(int index, Action<SessionSnapshot> update)
        {
            if (!_sessionSnapshots.TryGetValue(index, out var snapshot))
            {
                snapshot = new SessionSnapshot { Index = index };
                _sessionSnapshots[index] = snapshot;
            }

            update(snapshot);
            MarkUiDirty();
        }

        protected void AppendLog(string message)
        {
            if (_isShuttingDown)
                return;

            Debug.Log(message);
            _logEntries.Add(message);
            if (_logEntries.Count > MaxLogEntries)
                _logEntries.RemoveAt(0);
            MarkUiDirty();
        }

        private void BeginShutdown()
        {
            if (_cleanupStarted)
                return;

            _cleanupStarted = true;
            _isShuttingDown = true;

            foreach (var session in _sessions.ToArray())
                session.BeginShutdown();

            _ = CleanupAsync(true);
        }

        private async Task CleanupAsync(bool notifyDisconnected)
        {
            var sessions = _sessions.ToArray();
            _sessions.Clear();
            MarkUiDirty();

            foreach (var session in sessions)
            {
                try
                {
                    await session.DisposeAsync();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"RPC cleanup error: {ex}");
                }
            }

            if (notifyDisconnected)
                UpdateStatus(ConnectionState.Disconnected, "Disconnected");
        }

        private void OnSessionDisconnected(ConnectionSession session, Exception? ex)
        {
            _sessions.Remove(session);

            if (_isShuttingDown)
                return;

            if (ex is null)
                AppendLog($"Session[{session.Index}] disconnected.");
            else
                AppendLog($"Session[{session.Index}] disconnected: {ex.Message}");

            UpdateSession(session.Index, snapshot =>
            {
                snapshot.State = ex is null ? "Disconnected" : "Error";
                if (ex is not null)
                    snapshot.LastMessage = ex.Message;
            });

            if (_state == ConnectionState.Connecting)
            {
                UpdateStatus(ConnectionState.Error, "Connection closed during startup.");
                return;
            }

            if (_sessions.Count == 0)
                UpdateStatus(ex is null ? ConnectionState.Disconnected : ConnectionState.Error, ex?.Message ?? "Disconnected");
        }

        private void UpdateStatus(ConnectionState state, string? message)
        {
            _state = state;
            StatusChanged?.Invoke(state, message);
            if (!string.IsNullOrWhiteSpace(message))
                AppendLog($"State => {state}: {message}");
            MarkUiDirty();
        }

        private void EnsureDebugUi()
        {
            if (_debugUiBound && _debugDocument is not null)
                return;

            if (_debugDocument is null)
            {
                var documents = GetComponentsInChildren<UIDocument>(true);
                if (documents.Length > 0)
                {
                    _debugDocument = documents[0];

                    for (var i = 1; i < documents.Length; i++)
                    {
                        if (documents[i] is null)
                            continue;

                        documents[i].enabled = false;
                    }

                    if (documents.Length > 1)
                        Debug.LogWarning($"RpcConnectionTester found {documents.Length} UIDocuments under the same hierarchy. Extra documents were disabled.");
                }
            }

            if (_debugDocument is null)
            {
                if (!_isShuttingDown)
                    Debug.LogWarning("RpcConnectionTester requires a UIDocument in the scene hierarchy.");
                return;
            }

            var root = _debugDocument.rootVisualElement;
            AttachStyleSheet(root);

            _titleLabel = root.Q<Label>("titleLabel");
            _stateLabel = root.Q<Label>("stateLabel");
            _transportLabel = root.Q<Label>("transportLabel");
            _endpointLabel = root.Q<Label>("endpointLabel");
            _connectionsLabel = root.Q<Label>("connectionsLabel");
            _intervalLabel = root.Q<Label>("intervalLabel");
            _connectButton = root.Q<Button>("connectButton");
            _sessionsScroll = root.Q<ScrollView>("sessionsScroll");
            _logsScroll = root.Q<ScrollView>("logsScroll");
            EnsureSessionsContent();
            EnsureLogsContent();

            if (_connectButton is not null)
            {
                _connectButton.clicked -= ConnectFromUi;
                _connectButton.clicked += ConnectFromUi;
            }

            _uiToolkitReady = root.panel is not null &&
                              _titleLabel is not null &&
                              _connectButton is not null &&
                              _sessionsScroll is not null &&
                              _logsScroll is not null;
            _debugUiBound = true;
        }

        private void RefreshDebugUi()
        {
            _uiDirty = false;
            EnsureDebugUi();

            if (_debugDocument is null)
                return;

            var root = _debugDocument.rootVisualElement;
            AttachStyleSheet(root);
            _uiToolkitReady = root.panel is not null &&
                              _titleLabel is not null &&
                              _connectButton is not null &&
                              _sessionsScroll is not null &&
                              _logsScroll is not null;
            root.style.display = ShowDebugPanel ? DisplayStyle.Flex : DisplayStyle.None;

            if (!ShowDebugPanel)
                return;

            if (_titleLabel is not null)
                _titleLabel.text = RuntimeTitle;
            if (_stateLabel is not null)
                _stateLabel.text = $"State\n{_state}";
            if (_transportLabel is not null)
                _transportLabel.text = $"Transport\n{TransportKind}";
            if (_endpointLabel is not null)
                _endpointLabel.text = $"Endpoint\n{Endpoint.GetDisplayAddress(TransportKind)}";
            if (_connectionsLabel is not null)
                _connectionsLabel.text = $"Connections\n{_sessions.Count}/{Mathf.Max(1, ConnectionCount)}";
            if (_intervalLabel is not null)
                _intervalLabel.text = $"Interval\n{RequestIntervalSeconds:0.##}s";

            if (_connectButton is not null)
            {
                var canConnect = _state == ConnectionState.Idle ||
                                 _state == ConnectionState.Disconnected ||
                                 _state == ConnectionState.Error;
                _connectButton.SetEnabled(canConnect);
            }

            RebuildSessions();
            RebuildLogs();
        }

        private void RebuildSessions()
        {
            if (_sessionsContent is null)
                return;

            var activeKeys = new HashSet<int>();

            foreach (var entry in GetOrderedSessionSnapshots())
            {
                var snapshot = entry.Value;
                activeKeys.Add(snapshot.Index);
                if (!_sessionViews.TryGetValue(snapshot.Index, out var view))
                {
                    view = CreateSessionView(snapshot.Index);
                    _sessionViews[snapshot.Index] = view;
                    _sessionsContent.Add(view.Root);
                }

                view.Title.text = $"Session {snapshot.Index + 1}";
                view.Account.text = snapshot.Account ?? "?";
                view.State.text = snapshot.State ?? "Idle";
                view.Step.text = $"step={snapshot.LastStep}";
                view.Message.text = snapshot.LastMessage ?? string.Empty;
                view.Message.style.display = string.IsNullOrWhiteSpace(snapshot.LastMessage)
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;
            }

            var staleKeys = new List<int>();
            foreach (var key in _sessionViews.Keys)
            {
                if (!activeKeys.Contains(key))
                    staleKeys.Add(key);
            }

            foreach (var key in staleKeys)
            {
                var view = _sessionViews[key];
                view.Root.RemoveFromHierarchy();
                _sessionViews.Remove(key);
            }
        }

        private void RebuildLogs()
        {
            if (_logsLabel is null)
                return;

            _logsLabel.text = string.Join("\n", _logEntries);
        }

        private static Label CreateValueLabel(string text, string className)
        {
            var label = new Label(text);
            label.AddToClassList(className);
            return label;
        }

        private IEnumerable<KeyValuePair<int, SessionSnapshot>> GetOrderedSessionSnapshots()
        {
            var keys = new List<int>(_sessionSnapshots.Keys);
            keys.Sort();

            foreach (var key in keys)
                yield return new KeyValuePair<int, SessionSnapshot>(key, _sessionSnapshots[key]);
        }

        private void MarkUiDirty()
        {
            _uiDirty = true;
        }

        private void TeardownDebugUi()
        {
            _titleLabel = null;
            _stateLabel = null;
            _transportLabel = null;
            _endpointLabel = null;
            _connectionsLabel = null;
            _intervalLabel = null;
            _connectButton = null;
            _sessionsScroll = null;
            _logsScroll = null;
            _sessionsContent = null;
            _logsLabel = null;
            _sessionViews.Clear();
            _styleSheetAttached = false;
            _uiToolkitReady = false;
            _debugUiBound = false;
        }

        private void AttachStyleSheet(VisualElement root)
        {
            if (_styleSheetAttached)
                return;

            var styleSheet = Resources.Load<StyleSheet>("RpcConnectionTesterPanel");
            if (styleSheet is null)
                return;

            root.styleSheets.Add(styleSheet);
            _styleSheetAttached = true;
        }

        private void EnsureSessionsContent()
        {
            if (_sessionsScroll is null)
                return;

            _sessionsContent = _sessionsScroll.Q<VisualElement>("sessionsContent");
            if (_sessionsContent is not null)
                return;

            _sessionsContent = new VisualElement
            {
                name = "sessionsContent"
            };
            _sessionsContent.AddToClassList("rpc-list-content");
            _sessionsScroll.contentContainer.Clear();
            _sessionsScroll.contentContainer.Add(_sessionsContent);
        }

        private void EnsureLogsContent()
        {
            if (_logsScroll is null)
                return;

            _logsLabel = _logsScroll.Q<Label>("logsLabel");
            if (_logsLabel is not null)
                return;

            _logsLabel = new Label
            {
                name = "logsLabel"
            };
            _logsLabel.AddToClassList("rpc-log-content");
            _logsScroll.contentContainer.Clear();
            _logsScroll.contentContainer.Add(_logsLabel);
        }

        private static SessionView CreateSessionView(int index)
        {
            var root = new VisualElement();
            root.AddToClassList("rpc-session-card");

            var title = new Label($"Session {index + 1}");
            title.AddToClassList("rpc-session-title");
            root.Add(title);

            var account = CreateValueLabel("?", "rpc-session-account");
            var state = CreateValueLabel("Idle", "rpc-session-state");
            var step = CreateValueLabel("step=0", "rpc-session-step");
            var message = CreateValueLabel(string.Empty, "rpc-session-message");

            root.Add(account);
            root.Add(state);
            root.Add(step);
            root.Add(message);

            return new SessionView(root, title, account, state, step, message);
        }

        protected sealed class SessionSnapshot
        {
            public string? Account;
            public int Index;
            public string? LastMessage;
            public int LastStep;
            public string? State;
        }

        private sealed class SessionView
        {
            public SessionView(
                VisualElement root,
                Label title,
                Label account,
                Label state,
                Label step,
                Label message)
            {
                Root = root;
                Title = title;
                Account = account;
                State = state;
                Step = step;
                Message = message;
            }

            public VisualElement Root { get; }
            public Label Title { get; }
            public Label Account { get; }
            public Label State { get; }
            public Label Step { get; }
            public Label Message { get; }
        }

        private sealed class ConnectionSession : IAsyncDisposable
        {
            private readonly RpcConnectionTesterBase _owner;
            private readonly CancellationTokenSource _cts = new();
            private readonly RpcClient.RpcNotificationBindings _callbacks;
            private RpcClient? _connection;
            private bool _disposed;
            private Task? _pollingTask;
            private IPlayerService? _proxy;
            private bool _stopped;

            public ConnectionSession(RpcConnectionTesterBase owner, int index)
            {
                _owner = owner;
                Index = index;
                _callbacks = new RpcClient.RpcNotificationBindings();
                _callbacks.Add(new PlayerCallbacks(this));
            }

            public int Index { get; }

            public async Task StartAsync()
            {
                _connection = new RpcClient(_owner.CreateClientOptions(), _callbacks);
                await _connection.ConnectAsync(_cts.Token);
                _connection.Disconnected += OnDisconnected;
                _proxy = _connection.Api.Game.Player;

                var account = $"{_owner.Account}-{Index + 1}";
                var reply = await _proxy.LoginAsync(new LoginRequest
                {
                    Account = account,
                    Password = _owner.Password
                });

                _owner.UpdateSession(Index, snapshot =>
                {
                    snapshot.Account = account;
                    snapshot.State = "Connected";
                    snapshot.LastMessage = $"token={reply.Token}";
                });
                _owner.AppendLog($"Session[{Index}] login ok: code={reply.Code}, token={reply.Token}");
                _pollingTask = RunPollingAsync(account);
            }

            private void HandleNotify(string message)
            {
                if (_stopped || _owner._isShuttingDown)
                    return;

                _owner.UpdateSession(Index, snapshot => snapshot.LastMessage = message);
                _owner.AppendLog($"Session[{Index}] server push: {message}");
            }

            public void BeginShutdown()
            {
                if (_stopped)
                    return;

                _stopped = true;
                _cts.Cancel();

                if (_connection is not null)
                    _connection.Disconnected -= OnDisconnected;
            }

            public async ValueTask DisposeAsync()
            {
                if (_disposed)
                    return;

                _disposed = true;
                BeginShutdown();

                if (_pollingTask is not null)
                {
                    try
                    {
                        await _pollingTask;
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }

                if (_connection is not null)
                    await _connection.DisposeAsync();

                _cts.Dispose();
            }

            private async Task RunPollingAsync(string account)
            {
                var interval = Mathf.Max(0.1f, _owner.RequestIntervalSeconds);

                while (!_cts.IsCancellationRequested && !_stopped)
                {
                    try
                    {
                        var step = await _proxy!.IncrStep(new StepRequest());
                        if (_cts.IsCancellationRequested || _stopped || _owner._isShuttingDown)
                            return;

                        _owner.UpdateSession(Index, snapshot =>
                        {
                            snapshot.Account = account;
                            snapshot.LastStep = step.Step;
                            snapshot.State = "Polling";
                        });
                        _owner.AppendLog($"Session[{Index}] {account} step={step.Step}");
                        await Task.Delay(TimeSpan.FromSeconds(interval), _cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }

            private void OnDisconnected(Exception? ex)
            {
                if (_stopped)
                    return;

                _stopped = true;
                _owner.OnSessionDisconnected(this, ex);
            }

            private sealed class PlayerCallbacks : RpcClient.PlayerNotificationsBase
            {
                private readonly ConnectionSession _owner;

                public PlayerCallbacks(ConnectionSession owner)
                {
                    _owner = owner;
                }

                public override void OnNotify(PlayerNotify notify) => _owner.HandleNotify(notify.Message);
            }
        }
    }
}
