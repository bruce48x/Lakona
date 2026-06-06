#nullable enable

using System;
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
                _owner._status = "请输入账号信息";
                _owner._eventMessage = "点击匹配开始联机";
                RefreshSceneUi();
            }

            public void OnUiBackToModeSelect()
            {
                if (_owner.IsUiBusy)
                {
                    return;
                }

                _owner._entryMenuState = EntryMenuState.ModeSelect;
                _owner._status = "请选择模式";
                _owner._eventMessage = "请选择单机或联机";
                RefreshSceneUi();
            }

            public void OnUiCancelMatchmakingRequested()
            {
                if (_owner._flowState != FrontendFlowState.Matchmaking || _owner.HasPendingUiRequest)
                {
                    return;
                }

                _owner._pendingUiRequest = PendingUiRequest.CancelMatchmaking;
                _owner._status = "正在取消匹配";
                _owner._eventMessage = "正在返回联机大厅";
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
                _owner._status = $"正在连接 {Rpc.WebSocketRpcClientFactory.BuildUrl(_owner._host, _owner._port, _owner._path)}";
                _owner._eventMessage = "正在登录联机账号";
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
                _owner._status = $"正在连接 {Rpc.WebSocketRpcClientFactory.BuildUrl(_owner._host, _owner._port, _owner._path)}";
                _owner._eventMessage = "正在申请游客账号";
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
                    case MetaTab.Settings:
                        HandleSettingsLobbyAction(isPrimaryAction);
                        break;
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
                    _owner.PushEvent($"下一本地预设：{DotArenaSinglePlayerCatalog.GetPresetLabel(previewPreset.MapVariant, previewPreset.RuleVariant)}", 4f);
                    return;
                }

                var selectedPreset = DotArenaSinglePlayerCatalog.AdvancePresetSelection(ref _owner._singlePlayerPlaylistIndex);
                _owner.PushEvent($"已切换预设：{DotArenaSinglePlayerCatalog.GetPresetLabel(selectedPreset.MapVariant, selectedPreset.RuleVariant)}", 4f);
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
                _owner._status = "正在退出联机大厅";
                _owner._eventMessage = "正在断开连接并注销会话";
                RefreshSceneUi();
                _ = ExitMultiplayerLobbyAsync();
            }

            public async Task ExitMultiplayerLobbyAsync()
            {
                try
                {
                    await _owner.DisposeConnectionAsync(clearSessionState: false, logout: true);
                    _owner.ResetToModeSelect(
                        status: "选择模式",
                        eventMessage: "已退出联机大厅",
                        toastMessage: "已断开连接并退出联机大厅");
                }
                finally
                {
                    _owner._pendingUiRequest = PendingUiRequest.None;
                }
            }

            public void HandleSettingsLobbyAction(bool isPrimaryAction)
            {
                if (_owner._metaState == null)
                {
                    return;
                }

                if (isPrimaryAction)
                {
                    var nextLanguage = string.Equals(_owner._metaState.Settings.Language, "zh-CN", StringComparison.Ordinal)
                        ? "en-US"
                        : "zh-CN";
                    if (DotArenaMetaProgression.SetLanguage(_owner._metaState, nextLanguage))
                    {
                        _owner.PushEvent($"语言已切换为 {nextLanguage}");
                    }

                    return;
                }

                var fullscreen = DotArenaMetaProgression.ToggleFullscreen(_owner._metaState);
                _owner.PushEvent(fullscreen ? "已开启全屏" : "已关闭全屏");
            }

        }
    }
}
