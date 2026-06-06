#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Shared.Gameplay;
using Shared.Interfaces;
using UnityEngine;
using static SampleClient.Gameplay.DotArenaTuning;

namespace SampleClient.Gameplay
{
    public sealed partial class DotArenaGame : MonoBehaviour, IPlayerCallback
    {
        [SerializeField] private string _host = "127.0.0.1";
        [SerializeField] private int _port = 20000;
        [SerializeField] private string _path = "/ws";
        [SerializeField] private string _account = "";
        [SerializeField] private string _password = "";

        private readonly CancellationTokenSource _cts = new();
        private readonly DotArenaCallbackInbox _callbackInbox = new();
        private readonly DotArenaSceneUiPresenter _sceneUiPresenter = new();
        private readonly DotArenaPlayerOverlayPresenter _overlayPresenter = new();
        private readonly DotArenaMultiplayerState _multiplayerState = new();
        private readonly Dictionary<string, DotView> _views = new(StringComparer.Ordinal);
        private readonly Dictionary<string, PlayerRenderState> _renderStates = new(StringComparer.Ordinal);
        private readonly List<PickupView> _pickupViews = new();

        private DotArenaNetworkSession? _networkSession;
        private DotArenaWorldSynchronizer? _worldSynchronizer;
        private ArenaSimulation? _localMatch;
        private bool _singlePlayerStartRequested;
        private bool _rematchRequested;
        private bool _returnToLobbyRequested;
        private SinglePlayerMode _requestedSinglePlayerMode = SinglePlayerMode.Normal;
        private SinglePlayerMode _currentSinglePlayerMode = SinglePlayerMode.Normal;
        private EntryMenuState _entryMenuState = EntryMenuState.ModeSelect;
        private FrontendFlowState _flowState = FrontendFlowState.Entry;
        private int _inputTick;
        private float _nextInputAt;

        private Sprite _pixelSprite = null!;
        private Sprite _playerSprite = null!;
        private Sprite _playerOutlineSprite = null!;
        private Sprite? _massPickupSprite;
        private Sprite? _goldPickupSprite;
        private Sprite? _arenaBackgroundSprite;
        private Sprite? _pickupGlowSprite;
        private Sprite? _spawnWaveSprite;
        private readonly Dictionary<string, Sprite> _playerSkinSprites = new(StringComparer.Ordinal);
        private readonly List<Sprite> _remotePlayerSkinSprites = new();
        private Shader? _jellyShader;
        private Shader? _pickupAbsorbShader;
        private string _status = "连接中...";
        private string _eventMessage = "等待玩家加入";
        private float _eventMessageUntil;
        private int _lastWorldTick = -1;
        private int _lastLoggedPlayerCount = -1;
        private bool _shutdownStarted;
        private bool _ignoreDisconnectCallback;
        private string _lastLoggedInputVector = string.Empty;
        private bool _showDebugPanel;
        private int _lastRoundRemainingSeconds;
        private MatchSettlementSummary? _settlementSummary;
        private DotArenaMetaState? _metaState;
        private DotArenaRewardSummary? _lastRewardSummary;
        private MetaTab _selectedMetaTab;
        private SpriteRenderer? _safeZoneRenderer;
        private SpriteRenderer? _topBorderRenderer;
        private SpriteRenderer? _bottomBorderRenderer;
        private SpriteRenderer? _leftBorderRenderer;
        private SpriteRenderer? _rightBorderRenderer;
        private Vector2 _currentArenaHalfExtents = GameplayConfig.ArenaHalfExtents;
        private int _singlePlayerPlaylistIndex = -1;
        private ArenaMapVariant _currentArenaMapVariant = ArenaMapVariant.ClassicSquare;
        private ArenaRuleVariant _currentArenaRuleVariant = ArenaRuleVariant.ClassicElimination;
#if UNITY_EDITOR
        private Vector2 _editorMoveOverride;
        private bool _hasEditorInputOverride;
#endif

        private bool HasActiveSession => _flowState is FrontendFlowState.Matchmaking or FrontendFlowState.InMatch;
        private DotArenaNetworkSession NetworkSession => _networkSession ??= new DotArenaNetworkSession(OnDisconnected);
        private bool IsConnected => NetworkSession.IsConnected;
        private bool IsConnecting => NetworkSession.IsConnecting;
        private bool IsRealtimeConnected => NetworkSession.IsRealtimeConnected;
        private bool CanSubmitGameplayInput => NetworkSession.CanSubmitGameplayInput;
        private bool HasPendingUiRequest => _multiplayerState.HasPendingUiRequest;
        private bool IsUiBusy => IsConnecting || HasPendingUiRequest || _controlReconnectInProgress;
        private string _localPlayerId { get => _multiplayerState.LocalPlayerId; set => _multiplayerState.LocalPlayerId = value; }
        private SessionMode _sessionMode { get => _multiplayerState.SessionMode; set => _multiplayerState.SessionMode = value; }
        private float _matchmakingStartedAt { get => _multiplayerState.MatchmakingStartedAt; set => _multiplayerState.MatchmakingStartedAt = value; }
        private int _localWinCount { get => _multiplayerState.LocalWinCount; set => _multiplayerState.LocalWinCount = value; }
        private bool _hasAuthenticatedProfile { get => _multiplayerState.HasAuthenticatedProfile; set => _multiplayerState.HasAuthenticatedProfile = value; }
        private string _authenticatedPlayerId { get => _multiplayerState.AuthenticatedPlayerId; set => _multiplayerState.AuthenticatedPlayerId = value; }
        private PendingUiRequest _pendingUiRequest { get => _multiplayerState.PendingUiRequest; set => _multiplayerState.PendingUiRequest = value; }
        private bool _controlReconnectInProgress { get => _multiplayerState.ControlReconnectInProgress; set => _multiplayerState.ControlReconnectInProgress = value; }
        private RealtimeConnectionInfo? _lastRealtimeConnection { get => _multiplayerState.LastRealtimeConnection; set => _multiplayerState.LastRealtimeConnection = value; }

        private DotArenaWorldSynchronizer WorldSynchronizer => _worldSynchronizer ??= new DotArenaWorldSynchronizer(
            _views,
            _renderStates,
            _pickupViews,
            _overlayPresenter.Views,
            CreateView,
            playerId => _overlayPresenter.EnsureOverlay(_sceneUiPresenter, playerId),
            CreatePickupView,
            Destroy,
            UpdateArenaVisuals,
            message => PushEvent(message),
            (message, duration) => PushEvent(message, duration),
            message => _eventMessage = message,
            GetLocalPresentationCosmeticId);

        private void Start()
        {
            ApplyLaunchOverrides();
            ConfigureWindow();
            InitializeConnectionMode();
            EnsureMetaState("Guest");
            ConfigureCamera();
            BuildArena();
            BindSceneUi();
            RefreshSceneUi();
        }

        private void Update()
        {
            CaptureInputIntent();
            ProcessMenuRequests();
            HandleInput();
            TickLocalMatch();
            ApplyPendingCallbacks();
            UpdateViews();
            RefreshSceneUi();
        }

        private void OnDestroy()
        {
            BeginShutdown();
            _cts.Dispose();
        }

        private void OnApplicationQuit()
        {
            BeginShutdown();
        }
    }
}
