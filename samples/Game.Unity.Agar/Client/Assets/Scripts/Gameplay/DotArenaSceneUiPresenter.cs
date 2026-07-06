#nullable enable

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static SampleClient.Gameplay.DotArenaTuning;

namespace SampleClient.Gameplay
{
    internal struct DotArenaSceneUiSnapshot
    {
        public bool HasSession { get; set; }
        public FrontendFlowState FlowState { get; set; }
        public EntryMenuState EntryMenuState { get; set; }
        public SessionMode SessionMode { get; set; }
        public string Status { get; set; }
        public string LocalPlayerId { get; set; }
        public string Account { get; set; }
        public string Password { get; set; }
        public string LocalPlayerMassText { get; set; }
        public int LocalWinCount { get; set; }
        public int LastRoundRemainingSeconds { get; set; }
        public bool IsConnecting { get; set; }
        public bool IsBusy { get; set; }
        public string SettlementTitle { get; set; }
        public string SettlementDetail { get; set; }
        public string SettlementRewardSummary { get; set; }
        public string SettlementTaskSummary { get; set; }
        public string SettlementNextStepSummary { get; set; }
        public string SettlementPrimaryActionText { get; set; }
        public string MatchmakingTitle { get; set; }
        public string MatchmakingDetail { get; set; }
        public int MatchmakingElapsedSeconds { get; set; }
        public string MetaPlayerSummary { get; set; }
        public string MetaProfileDetail { get; set; }
        public string MetaTasksDetail { get; set; }
        public string MetaShopDetail { get; set; }
        public string MetaRecordsDetail { get; set; }
        public string MetaLeaderboardDetail { get; set; }
        public string MetaSettingsDetail { get; set; }
        public string MetaFooterHint { get; set; }
        public IReadOnlyList<DotArenaMatchRankingEntry>? MatchRankingEntries { get; set; }
    }

    internal readonly struct DotArenaMatchRankingEntry
    {
        public DotArenaMatchRankingEntry(int rank, string playerId, float mass, bool isLocalPlayer)
        {
            Rank = rank;
            PlayerId = playerId;
            Mass = mass;
            IsLocalPlayer = isLocalPlayer;
        }

        public int Rank { get; }
        public string PlayerId { get; }
        public float Mass { get; }
        public bool IsLocalPlayer { get; }
    }

    internal sealed partial class DotArenaSceneUiPresenter
    {
        private const int MatchRankingMaxRows = 10;

        private Transform? _owner;
        private GameObject? _sceneUiRoot;
        private GameObject? _menuBackground;
        private GameObject? _topStatusPanel;
        private GameObject? _hudPanel;
        private GameObject? _matchRankingPanel;
        private GameObject? _matchmakingPanel;
        private GameObject? _lobbyPanel;
        private GameObject? _modeSelectPanel;
        private GameObject? _loginPanel;
        private TMP_Text? _matchmakingTitleText;
        private TMP_Text? _matchmakingDetailText;
        private Button? _matchmakingCancelButton;
        private TMP_Text? _matchmakingCancelButtonText;
        private TMP_Text? _lobbyTitleText;
        private TMP_Text? _lobbySummaryText;
        private TMP_Text? _lobbyQuickActionsText;
        private Button? _lobbyQuickActionButton1;
        private Button? _lobbyQuickActionButton2;
        private Button? _lobbyQuickActionButton3;
        private Button? _lobbyQuickActionButton4;
        private TMP_Text? _lobbyQuickActionButton1Text;
        private TMP_Text? _lobbyQuickActionButton2Text;
        private TMP_Text? _lobbyQuickActionButton3Text;
        private TMP_Text? _lobbyQuickActionButton4Text;
        private TMP_Text? _lobbyDetailText;
        private TMP_Text? _lobbyFooterText;
        private Button? _lobbyPrimaryActionButton;
        private Button? _lobbySecondaryActionButton;
        private TMP_Text? _lobbyPrimaryActionButtonText;
        private TMP_Text? _lobbySecondaryActionButtonText;
        private Button? _lobbyProfileButton;
        private Button? _lobbyTasksButton;
        private Button? _lobbyShopButton;
        private Button? _lobbyRecordsButton;
        private Button? _lobbyLeaderboardButton;
        private Button? _lobbySettingsButton;
        private TMP_Text? _hudTitleText;
        private TMP_Text? _hudModeText;
        private TMP_Text? _hudHintText;
        private TMP_Text? _hudCountdownText;
        private TMP_Text? _matchRankingTitleText;
        private TMP_Text? _matchRankingHeaderText;
        private TMP_Text? _entryTitleText;
        private TMP_Text? _entryStatusText;
        private TMP_Text? _multiplayerSubtitleText;
        private TMP_Text? _accountLabelText;
        private TMP_Text? _passwordLabelText;
        private TMP_Text? _accountPlaceholderText;
        private TMP_Text? _passwordPlaceholderText;
        private Button? _singlePlayerButton;
        private Button? _invincibleSinglePlayerButton;
        private Button? _multiplayerButton;
        private Button? _matchButton;
        private Button? _guestLoginButton;
        private Button? _backButton;
        private GameObject? _settlementPanel;
        private TMP_Text? _settlementTitleText;
        private TMP_Text? _settlementDetailText;
        private TMP_Text? _settlementRewardText;
        private TMP_Text? _settlementTaskText;
        private TMP_Text? _settlementNextStepText;
        private Button? _settlementPrimaryButton;
        private Button? _settlementSecondaryButton;
        private TMP_Text? _settlementPrimaryButtonText;
        private TMP_Text? _settlementSecondaryButtonText;
        private TMP_Text? _singlePlayerButtonText;
        private TMP_Text? _invincibleSinglePlayerButtonText;
        private TMP_Text? _multiplayerButtonText;
        private TMP_Text? _matchButtonText;
        private TMP_Text? _guestLoginButtonText;
        private TMP_Text? _backButtonText;
        private TMP_InputField? _accountInputField;
        private TMP_InputField? _passwordInputField;
        private InputField? _accountLegacyInputField;
        private InputField? _passwordLegacyInputField;
        private TMP_FontAsset? _tmpFontAsset;
        private Sprite? _uiPanelSprite;
        private Sprite? _uiButtonSprite;
        private DotArenaUiFactory? _uiFactory;
        private readonly DotArenaSceneLobbyUiCoordinator _lobbyUi = new();
        private readonly List<MatchRankingRowUi> _matchRankingRows = new();

        public bool HasSceneUi => _sceneUiRoot != null;

        public RectTransform? OverlayLayer { get; private set; }

        private DotArenaUiFactory UiFactory => _uiFactory ??= new DotArenaUiFactory(() => _tmpFontAsset ??= LoadTmpFontAsset());

        public void Bind(
            Transform owner,
            Action onSinglePlayerSelected,
            Action onInvincibleSinglePlayerSelected,
            Action onMultiplayerSelected,
            Action onConnectRequested,
            Action onGuestLoginRequested,
            Action onBackToModeSelect,
            Action onCancelMatchmakingRequested,
            Action<string> onAccountChanged,
            Action<string> onPasswordChanged,
            Action<MetaTab, bool> onLobbyActionRequested,
            Action onRematchRequested,
            Action onReturnToLobbyRequested)
        {
            _owner = owner;
            _sceneUiRoot = FindSceneUiObject("SceneUI");
            if (_sceneUiRoot == null)
            {
                return;
            }

            _tmpFontAsset ??= LoadTmpFontAsset();
            ApplySceneUiFonts();
            EnsureRuntimeUiSprites();
            EnsureMenuBackground();

            OverlayLayer = FindSceneUiRect("SceneUI/OverlayLayer");
            _hudPanel = FindSceneUiObject("SceneUI/HUDPanel");
            EnsureTopStatusPanel();
            EnsureMatchRankingPanel();
            _matchRankingPanel = FindSceneUiObject("SceneUI/MatchRankingPanel");
            EnsureMatchmakingPanel();
            _matchmakingPanel = FindSceneUiObject("SceneUI/MatchmakingPanel");
            EnsureLobbyPanel();
            _lobbyPanel = FindSceneUiObject("SceneUI/LobbyPanel");
            EnsureLobbyQuickActionsText();
            WarnIfMissingLobbyQuickActionButtons();
            _modeSelectPanel = FindSceneUiObject("SceneUI/ModeSelectPanel");
            _loginPanel = FindSceneUiObject("SceneUI/LoginPanel");
            WarnIfMissingAuthoredScreen("SceneUI/ModeSelectPanel", _modeSelectPanel);
            WarnIfMissingAuthoredScreen("SceneUI/LoginPanel", _loginPanel);
            WarnIfMissingAuthoredScreen("SceneUI/MatchmakingPanel", _matchmakingPanel);
            WarnIfMissingAuthoredScreen("SceneUI/LobbyPanel", _lobbyPanel);
            EnsureSettlementPanel();
            WarnIfMissingAuthoredScreen("SceneUI/SettlementPanel", _settlementPanel);

            _hudTitleText = FindSceneUiText("SceneUI/HUDPanel/TitleText");
            _hudModeText = FindSceneUiText("SceneUI/HUDPanel/ModeText");
            _hudHintText = FindSceneUiText("SceneUI/HUDPanel/HintText");
            _hudCountdownText = FindSceneUiText("SceneUI/OverlayLayer/TopStatusPanel/CountdownText");
            _matchRankingTitleText = FindSceneUiText("SceneUI/MatchRankingPanel/TitleText");
            _matchRankingHeaderText = FindSceneUiText("SceneUI/MatchRankingPanel/HeaderText");
            EnsureTopStatusPanel();

            _entryTitleText = FindSceneUiText("SceneUI/LoginPanel/TitleText");
            _entryStatusText = FindSceneUiText("SceneUI/LoginPanel/StatusText");
            _matchmakingTitleText = FindSceneUiText("SceneUI/MatchmakingPanel/TitleText");
            _matchmakingDetailText = FindSceneUiText("SceneUI/MatchmakingPanel/DetailText");
            _matchmakingCancelButton = FindSceneUiButton("SceneUI/MatchmakingPanel/CancelButton");
            _matchmakingCancelButtonText = FindSceneUiText("SceneUI/MatchmakingPanel/CancelButton/Label");
            _lobbyTitleText = FindSceneUiText("SceneUI/LobbyPanel/TitleText");
            _lobbySummaryText = FindSceneUiText("SceneUI/LobbyPanel/SummaryText");
            _lobbyQuickActionsText = FindSceneUiText("SceneUI/LobbyPanel/QuickActionsText");
            _lobbyQuickActionButton1 = FindSceneUiButton("SceneUI/LobbyPanel/QuickActionButton1");
            _lobbyQuickActionButton2 = FindSceneUiButton("SceneUI/LobbyPanel/QuickActionButton2");
            _lobbyQuickActionButton3 = FindSceneUiButton("SceneUI/LobbyPanel/QuickActionButton3");
            _lobbyQuickActionButton4 = FindSceneUiButton("SceneUI/LobbyPanel/QuickActionButton4");
            _lobbyQuickActionButton1Text = FindSceneUiText("SceneUI/LobbyPanel/QuickActionButton1/Label");
            _lobbyQuickActionButton2Text = FindSceneUiText("SceneUI/LobbyPanel/QuickActionButton2/Label");
            _lobbyQuickActionButton3Text = FindSceneUiText("SceneUI/LobbyPanel/QuickActionButton3/Label");
            _lobbyQuickActionButton4Text = FindSceneUiText("SceneUI/LobbyPanel/QuickActionButton4/Label");
            _lobbyDetailText = FindSceneUiText("SceneUI/LobbyPanel/DetailText");
            _lobbyFooterText = FindSceneUiText("SceneUI/LobbyPanel/FooterText");
            _lobbyPrimaryActionButton = FindSceneUiButton("SceneUI/LobbyPanel/PrimaryActionButton");
            _lobbySecondaryActionButton = FindSceneUiButton("SceneUI/LobbyPanel/SecondaryActionButton");
            _lobbyPrimaryActionButtonText = FindSceneUiText("SceneUI/LobbyPanel/PrimaryActionButton/Label");
            _lobbySecondaryActionButtonText = FindSceneUiText("SceneUI/LobbyPanel/SecondaryActionButton/Label");
            _lobbyProfileButton = FindSceneUiButton("SceneUI/LobbyPanel/ProfileButton");
            _lobbyTasksButton = FindSceneUiButton("SceneUI/LobbyPanel/TasksButton");
            _lobbyShopButton = FindSceneUiButton("SceneUI/LobbyPanel/ShopButton");
            _lobbyRecordsButton = FindSceneUiButton("SceneUI/LobbyPanel/RecordsButton");
            _lobbyLeaderboardButton = FindSceneUiButton("SceneUI/LobbyPanel/LeaderboardButton");
            _lobbySettingsButton = FindSceneUiButton("SceneUI/LobbyPanel/SettingsButton");
            HideDeprecatedLobbyButton(_lobbyTasksButton);
            HideDeprecatedLobbyButton(_lobbyShopButton);
            HideDeprecatedLobbyButton(_lobbyRecordsButton);

            _multiplayerSubtitleText = FindSceneUiText("SceneUI/LoginPanel/SubtitleText");
            _accountLabelText = FindSceneUiText("SceneUI/LoginPanel/AccountLabel");
            _passwordLabelText = FindSceneUiText("SceneUI/LoginPanel/PasswordLabel");
            EnsureMultiplayerLabelLayout();
            _accountPlaceholderText = FindSceneUiText("SceneUI/LoginPanel/AccountInput/Text Area/Placeholder");
            _passwordPlaceholderText = FindSceneUiText("SceneUI/LoginPanel/PasswordInput/Text Area/Placeholder");

            _singlePlayerButton = FindSceneUiButton("SceneUI/ModeSelectPanel/SinglePlayerButton");
            _invincibleSinglePlayerButton = FindSceneUiButton("SceneUI/ModeSelectPanel/InvincibleSinglePlayerButton");
            _multiplayerButton = FindSceneUiButton("SceneUI/ModeSelectPanel/MultiplayerButton");
            _matchButton = FindSceneUiButton("SceneUI/LoginPanel/MatchButton");
            _guestLoginButton = FindSceneUiButton("SceneUI/LoginPanel/GuestLoginButton");
            _backButton = FindSceneUiButton("SceneUI/LoginPanel/BackButton");

            _singlePlayerButtonText = FindSceneUiText("SceneUI/ModeSelectPanel/SinglePlayerButton/Label");
            _invincibleSinglePlayerButtonText = FindSceneUiText("SceneUI/ModeSelectPanel/InvincibleSinglePlayerButton/Label");
            _multiplayerButtonText = FindSceneUiText("SceneUI/ModeSelectPanel/MultiplayerButton/Label");
            _matchButtonText = FindSceneUiText("SceneUI/LoginPanel/MatchButton/Label");
            _guestLoginButtonText = FindSceneUiText("SceneUI/LoginPanel/GuestLoginButton/Label");
            _backButtonText = FindSceneUiText("SceneUI/LoginPanel/BackButton/Label");

            _accountInputField = FindOrCreateSceneUiInputField(
                "SceneUI/LoginPanel/AccountInput",
                TMP_InputField.ContentType.Standard);
            _passwordInputField = FindOrCreateSceneUiInputField(
                "SceneUI/LoginPanel/PasswordInput",
                TMP_InputField.ContentType.Password);
            _accountLegacyInputField = _accountInputField == null
                ? FindOrCreateSceneUiLegacyInputField(
                    "SceneUI/LoginPanel/AccountInput",
                    InputField.ContentType.Standard)
                : null;
            _passwordLegacyInputField = _passwordInputField == null
                ? FindOrCreateSceneUiLegacyInputField(
                    "SceneUI/LoginPanel/PasswordInput",
                    InputField.ContentType.Password)
                : null;
            EnsureInputFieldViewport(_accountInputField);
            EnsureInputFieldViewport(_passwordInputField);
            EnsureLegacyInputFieldViewport(_accountLegacyInputField);
            EnsureLegacyInputFieldViewport(_passwordLegacyInputField);
            _settlementTitleText = FindSceneUiText("SceneUI/SettlementPanel/TitleText");
            _settlementDetailText = FindSceneUiText("SceneUI/SettlementPanel/DetailText");
            _settlementRewardText = FindSceneUiText("SceneUI/SettlementPanel/RewardText");
            _settlementTaskText = FindSceneUiText("SceneUI/SettlementPanel/TaskText");
            _settlementNextStepText = FindSceneUiText("SceneUI/SettlementPanel/NextStepText");
            _settlementPrimaryButton = FindSceneUiButton("SceneUI/SettlementPanel/PrimaryButton");
            _settlementSecondaryButton = FindSceneUiButton("SceneUI/SettlementPanel/SecondaryButton");
            _settlementPrimaryButtonText = FindSceneUiText("SceneUI/SettlementPanel/PrimaryButton/Label");
            _settlementSecondaryButtonText = FindSceneUiText("SceneUI/SettlementPanel/SecondaryButton/Label");

            ApplySceneUiTheme();

            if (_singlePlayerButton != null)
            {
                _singlePlayerButton.onClick.RemoveAllListeners();
                _singlePlayerButton.onClick.AddListener(() => onSinglePlayerSelected());
            }

            if (_invincibleSinglePlayerButton != null)
            {
                _invincibleSinglePlayerButton.onClick.RemoveAllListeners();
                _invincibleSinglePlayerButton.onClick.AddListener(() => onInvincibleSinglePlayerSelected());
            }

            if (_multiplayerButton != null)
            {
                _multiplayerButton.onClick.RemoveAllListeners();
                _multiplayerButton.onClick.AddListener(() => onMultiplayerSelected());
            }

            if (_matchButton != null)
            {
                _matchButton.onClick.RemoveAllListeners();
                _matchButton.onClick.AddListener(() => onConnectRequested());
            }

            if (_guestLoginButton != null)
            {
                _guestLoginButton.onClick.RemoveAllListeners();
                _guestLoginButton.onClick.AddListener(() => onGuestLoginRequested());
            }

            if (_backButton != null)
            {
                _backButton.onClick.RemoveAllListeners();
                _backButton.onClick.AddListener(() => onBackToModeSelect());
            }

            if (_matchmakingCancelButton != null)
            {
                _matchmakingCancelButton.onClick.RemoveAllListeners();
                _matchmakingCancelButton.onClick.AddListener(() => onCancelMatchmakingRequested());
            }

            _lobbyUi.BindLobbyTabButton(_lobbyProfileButton, MetaTab.Lobby);
            _lobbyUi.BindLobbyTabButton(_lobbyLeaderboardButton, MetaTab.Leaderboard);
            _lobbyUi.BindLobbyTabButton(_lobbySettingsButton, MetaTab.Settings);

            _lobbyUi.BindLobbyQuickActionButton(_lobbyQuickActionButton1, 0);
            _lobbyUi.BindLobbyQuickActionButton(_lobbyQuickActionButton2, 1);
            _lobbyUi.BindLobbyQuickActionButton(_lobbyQuickActionButton3, 2);
            _lobbyUi.BindLobbyQuickActionButton(_lobbyQuickActionButton4, 3);

            _lobbyUi.BindLobbyActionButtons(_lobbyPrimaryActionButton, _lobbySecondaryActionButton, onLobbyActionRequested);

            if (_accountInputField != null)
            {
                _accountInputField.onValueChanged.RemoveAllListeners();
                _accountInputField.onValueChanged.AddListener(value => onAccountChanged(value));
            }

            if (_passwordInputField != null)
            {
                _passwordInputField.onValueChanged.RemoveAllListeners();
                _passwordInputField.onValueChanged.AddListener(value => onPasswordChanged(value));
            }

            if (_accountLegacyInputField != null)
            {
                _accountLegacyInputField.onValueChanged.RemoveAllListeners();
                _accountLegacyInputField.onValueChanged.AddListener(value => onAccountChanged(value));
            }

            if (_passwordLegacyInputField != null)
            {
                _passwordLegacyInputField.onValueChanged.RemoveAllListeners();
                _passwordLegacyInputField.onValueChanged.AddListener(value => onPasswordChanged(value));
            }

            if (_settlementPrimaryButton != null)
            {
                _settlementPrimaryButton.onClick.RemoveAllListeners();
                _settlementPrimaryButton.onClick.AddListener(() => onRematchRequested());
            }

            if (_settlementSecondaryButton != null)
            {
                _settlementSecondaryButton.onClick.RemoveAllListeners();
                _settlementSecondaryButton.onClick.AddListener(() => onReturnToLobbyRequested());
            }
        }

        private static void HideDeprecatedLobbyButton(Button? button)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveAllListeners();
            button.gameObject.SetActive(false);
        }

        private static void WarnIfMissingAuthoredScreen(string path, GameObject? screen)
        {
            if (screen == null)
            {
                WarnMissingAuthoredSceneUi(path);
            }
        }

        private sealed class MatchRankingRowUi
        {
            public MatchRankingRowUi(GameObject root, Image background, TMP_Text rankText, TMP_Text nameText, TMP_Text massText)
            {
                Root = root;
                Background = background;
                RankText = rankText;
                NameText = nameText;
                MassText = massText;
            }

            public GameObject Root { get; }
            public Image Background { get; }
            public TMP_Text RankText { get; }
            public TMP_Text NameText { get; }
            public TMP_Text MassText { get; }
        }
    }
}
