#nullable enable

using System.Threading.Tasks;

namespace SampleClient.Gameplay
{
    public sealed partial class DotArenaGame
    {
        private DotArenaGameUiSurface? _uiSurface;

        private DotArenaGameUiSurface UiSurface => _uiSurface ??= new DotArenaGameUiSurface(this);

        private sealed partial class DotArenaGameUiSurface
        {
            private readonly DotArenaGame _owner;

            public DotArenaGameUiSurface(DotArenaGame owner)
            {
                _owner = owner;
            }

            public void BindSceneUi()
            {
                _owner._sceneUiPresenter.Bind(
                    _owner.transform,
                    OnUiSinglePlayerSelected,
                    OnUiInvincibleSinglePlayerSelected,
                    OnUiMultiplayerSelected,
                    OnUiConnectRequested,
                    OnUiGuestLoginRequested,
                    OnUiBackToModeSelect,
                    OnUiCancelMatchmakingRequested,
                    OnUiAccountChanged,
                    OnUiPasswordChanged,
                    OnUiLobbyTabSelected,
                    OnUiLobbyActionRequested,
                    OnUiRematchRequested,
                    OnUiReturnToLobbyRequested);
            }

            public void RefreshSceneUi()
            {
                _owner._sceneUiPresenter.Refresh(BuildSceneUiSnapshot());
            }

            public void OnUiSinglePlayerSelected()
            {
                if (_owner.IsUiBusy)
                {
                    return;
                }

                _owner._requestedSinglePlayerMode = SinglePlayerMode.Normal;
                _owner._currentSinglePlayerMode = SinglePlayerMode.Normal;
                _owner._singlePlayerStartRequested = true;
            }

            public void OnUiInvincibleSinglePlayerSelected()
            {
                if (_owner.IsUiBusy)
                {
                    return;
                }

                _owner._requestedSinglePlayerMode = SinglePlayerMode.Invincible;
                _owner._currentSinglePlayerMode = SinglePlayerMode.Invincible;
                _owner._singlePlayerStartRequested = true;
            }

            public void OnUiMultiplayerSelected()
            {
                if (_owner.IsUiBusy)
                {
                    return;
                }

                _owner._entryMenuState = EntryMenuState.MultiplayerAuth;
                _owner._status = "Enter account details";
                _owner._eventMessage = "Start matchmaking to play online";
                RefreshSceneUi();
            }

            public void OnUiBackToModeSelect()
            {
                if (_owner.IsUiBusy)
                {
                    return;
                }

                _owner._entryMenuState = EntryMenuState.ModeSelect;
                _owner._status = "Select a mode";
                _owner._eventMessage = "Choose single-player or multiplayer";
                RefreshSceneUi();
            }

            public void OnUiCancelMatchmakingRequested()
            {
                if (_owner._flowState != FrontendFlowState.Matchmaking || _owner.HasPendingUiRequest)
                {
                    return;
                }

                _owner._pendingUiRequest = PendingUiRequest.CancelMatchmaking;
                _owner._status = "Canceling matchmaking";
                _owner._eventMessage = "Returning to multiplayer lobby";
                RefreshSceneUi();
                _ = _owner.CancelMatchmakingAsync();
            }

            public void OnUiConnectRequested()
            {
                if (_owner.IsUiBusy)
                {
                    return;
                }

                _owner._pendingUiRequest = PendingUiRequest.Login;
                _owner._flowState = FrontendFlowState.Entry;
                _owner._entryMenuState = EntryMenuState.MultiplayerAuth;
                _owner._status = $"Connecting to {Rpc.WebSocketRpcClientFactory.BuildUrl(_owner._host, _owner._port, _owner._path)}";
                _owner._eventMessage = "Signing in to multiplayer";
                RefreshSceneUi();
                _ = _owner.ConnectAsync();
            }

            public void OnUiGuestLoginRequested()
            {
                if (_owner.IsUiBusy)
                {
                    return;
                }

                _owner._pendingUiRequest = PendingUiRequest.Login;
                _owner._flowState = FrontendFlowState.Entry;
                _owner._entryMenuState = EntryMenuState.MultiplayerAuth;
                _owner._status = $"Connecting to {Rpc.WebSocketRpcClientFactory.BuildUrl(_owner._host, _owner._port, _owner._path)}";
                _owner._eventMessage = "Requesting guest account";
                RefreshSceneUi();
                _ = _owner.ConnectAsGuestAsync();
            }

            public void OnUiRematchRequested()
            {
                if (_owner._flowState != FrontendFlowState.Settlement || _owner.IsUiBusy)
                {
                    return;
                }

                _owner._rematchRequested = true;
            }

            public void OnUiReturnToLobbyRequested()
            {
                if (_owner._flowState != FrontendFlowState.Settlement || _owner.IsUiBusy)
                {
                    return;
                }

                _owner._returnToLobbyRequested = true;
            }

            public void OnUiAccountChanged(string value)
            {
                _owner._account = value;
            }

            public void OnUiPasswordChanged(string value)
            {
                _owner._password = value;
            }

            public void OnUiLobbyActionRequested(MetaTab tab, bool isPrimaryAction)
            {
                if (_owner._metaState == null || _owner._flowState == FrontendFlowState.Matchmaking || _owner.HasPendingUiRequest)
                {
                    return;
                }

                switch (tab)
                {
                    case MetaTab.Lobby:
                        HandleLobbyPresetAction(isPrimaryAction);
                        break;
                }
            }

            public void OnUiLobbyTabSelected(MetaTab tab)
            {
                if (tab == MetaTab.Leaderboard && IsInMultiplayerLobby())
                {
                    _ = _owner.RefreshLeaderboardAsync();
                }
            }

            public void HandleLobbyPresetAction(bool isPrimaryAction)
            {
                if (IsInMultiplayerLobby())
                {
                    if (isPrimaryAction)
                    {
                        _owner.BeginMultiplayerMatchmaking();
                    }
                    else
                    {
                        LogOutToModeSelect();
                    }

                    return;
                }

                if (!isPrimaryAction)
                {
                    var previewPreset = DotArenaSinglePlayerCatalog.PeekPreset(_owner._singlePlayerPlaylistIndex);
                    _owner.PushEvent($"Next local preset: {DotArenaSinglePlayerCatalog.GetPresetLabel(previewPreset.MapVariant, previewPreset.RuleVariant)}", 4f);
                    return;
                }

                var selectedPreset = DotArenaSinglePlayerCatalog.AdvancePresetSelection(ref _owner._singlePlayerPlaylistIndex);
                _owner.PushEvent($"Preset changed: {DotArenaSinglePlayerCatalog.GetPresetLabel(selectedPreset.MapVariant, selectedPreset.RuleVariant)}", 4f);
            }

            public bool IsInMultiplayerLobby()
            {
                return _owner._flowState == FrontendFlowState.Entry &&
                       _owner._entryMenuState == EntryMenuState.MultiplayerLobby &&
                       _owner._sessionMode == SessionMode.Multiplayer &&
                       _owner._hasAuthenticatedProfile &&
                       !string.IsNullOrWhiteSpace(_owner._authenticatedPlayerId);
            }

            public void LogOutToModeSelect()
            {
                if (_owner.HasPendingUiRequest)
                {
                    return;
                }

                _owner._pendingUiRequest = PendingUiRequest.ExitLobby;
                _owner._status = "Leaving multiplayer lobby";
                _owner._eventMessage = "Disconnecting and signing out";
                RefreshSceneUi();
                _ = ExitMultiplayerLobbyAsync();
            }

            public async Task ExitMultiplayerLobbyAsync()
            {
                try
                {
                    await _owner.DisposeConnectionAsync(clearSessionState: false, logout: true);
                    _owner.ResetToModeSelect(
                        status: "Select mode",
                        eventMessage: "Left multiplayer lobby",
                        toastMessage: "Disconnected and left multiplayer lobby");
                }
                finally
                {
                    _owner._pendingUiRequest = PendingUiRequest.None;
                }
            }

        }
    }
}
