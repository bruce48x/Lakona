#nullable enable

using System;
using System.Threading.Tasks;
using Shared.Interfaces;
using UnityEngine;
using static SampleClient.Gameplay.DotArenaTuning;

namespace SampleClient.Gameplay
{
    public sealed partial class DotArenaGame
    {
        private async Task ConnectAsync()
        {
            if (IsConnecting || IsConnected || _sessionMode == SessionMode.SinglePlayer)
            {
                _pendingUiRequest = PendingUiRequest.None;
                return;
            }

            _flowState = FrontendFlowState.Entry;
            _entryMenuState = EntryMenuState.MultiplayerAuth;
            _status = $"正在连接 {Rpc.WebSocketRpcClientFactory.BuildUrl(_host, _port, _path)}";
            _eventMessage = "正在登录联机账号";
            _multiplayerState.SessionController.MarkConnecting();

            try
            {
                var reply = await NetworkSession.ConnectAndLoginAsync(_host, _port, _path, _account, _password, guestLogin: false, reconnect: false, this, _cts.Token);

                if (reply.Code != 0)
                {
                    _multiplayerState.ClearAuthenticatedProfile();
                    _multiplayerState.ClearSession();
                    _localMatch = null;
                    _status = $"登录失败, code={reply.Code}";
                    _eventMessage = "登录失败，请检查账号或密码";
                    return;
                }

                var playerId = string.IsNullOrWhiteSpace(reply.PlayerId) ? _account : reply.PlayerId;
                _multiplayerState.ApplyMultiplayerLogin(playerId, reply.Token, reply.SessionId, reply.SessionGeneration, reply.WinCount);
                _localMatch = null;
                EnsureMetaState(_localPlayerId);
                _ = RefreshLeaderboardAsync();
                _flowState = FrontendFlowState.Entry;
                _entryMenuState = EntryMenuState.MultiplayerLobby;
                _status = $"联机大厅: {_localPlayerId}";
                _eventMessage = "登录成功，可点击开始匹配进入队列";
                Debug.Log($"[DotArena] Connected as {_localPlayerId} -> {Rpc.WebSocketRpcClientFactory.BuildUrl(_host, _port, _path)}");
                PushEvent("登录成功，可在联机大厅开始匹配");
            }
            catch (OperationCanceledException)
            {
                _flowState = FrontendFlowState.Entry;
                _entryMenuState = EntryMenuState.MultiplayerAuth;
                _status = "连接已取消";
                _eventMessage = "登录已取消";
            }
            catch (Exception ex)
            {
                var feedback = DotArenaMultiplayerFlow.BuildConnectionFailure(ex);
                _flowState = FrontendFlowState.Entry;
                _entryMenuState = EntryMenuState.MultiplayerAuth;
                _status = feedback.Status;
                _eventMessage = feedback.Message;
                Debug.LogError($"[DotArena] Connect failed: {ex}");
                await DisposeConnectionAsync();
                _localMatch = null;
            }
            finally
            {
                if (_pendingUiRequest == PendingUiRequest.Login)
                {
                    _pendingUiRequest = PendingUiRequest.None;
                }
            }
        }

        private async Task ConnectAsGuestAsync()
        {
            if (IsConnecting || IsConnected || _sessionMode == SessionMode.SinglePlayer)
            {
                _pendingUiRequest = PendingUiRequest.None;
                return;
            }

            _flowState = FrontendFlowState.Entry;
            _entryMenuState = EntryMenuState.MultiplayerAuth;
            _status = $"正在连接 {Rpc.WebSocketRpcClientFactory.BuildUrl(_host, _port, _path)}";
            _eventMessage = "正在申请游客账号";
            _multiplayerState.SessionController.MarkConnecting();

            try
            {
                var reply = await NetworkSession.ConnectAndLoginAsync(_host, _port, _path, string.Empty, string.Empty, guestLogin: true, reconnect: false, this, _cts.Token);

                if (reply.Code != 0)
                {
                    _multiplayerState.ClearAuthenticatedProfile();
                    _multiplayerState.ClearSession();
                    _localMatch = null;
                    _status = $"游客登录失败, code={reply.Code}";
                    _eventMessage = "无法申请游客账号，请稍后重试";
                    return;
                }

                _account = string.IsNullOrWhiteSpace(reply.Account) ? reply.PlayerId : reply.Account;
                _password = reply.Password;
                var playerId = string.IsNullOrWhiteSpace(reply.PlayerId) ? _account : reply.PlayerId;
                _multiplayerState.ApplyMultiplayerLogin(playerId, reply.Token, reply.SessionId, reply.SessionGeneration, reply.WinCount);
                _localMatch = null;
                EnsureMetaState(_localPlayerId);
                _ = RefreshLeaderboardAsync();
                _flowState = FrontendFlowState.Entry;
                _entryMenuState = EntryMenuState.MultiplayerLobby;
                _status = $"联机大厅: {_localPlayerId}";
                _eventMessage = "游客登录成功，可点击开始匹配进入队列";
                Debug.Log($"[DotArena] Connected as guest {_localPlayerId} -> {Rpc.WebSocketRpcClientFactory.BuildUrl(_host, _port, _path)}");
                PushEvent("游客登录成功，可在联机大厅开始匹配");
            }
            catch (OperationCanceledException)
            {
                _flowState = FrontendFlowState.Entry;
                _entryMenuState = EntryMenuState.MultiplayerAuth;
                _status = "连接已取消";
                _eventMessage = "游客登录已取消";
            }
            catch (Exception ex)
            {
                var feedback = DotArenaMultiplayerFlow.BuildConnectionFailure(ex);
                _flowState = FrontendFlowState.Entry;
                _entryMenuState = EntryMenuState.MultiplayerAuth;
                _status = feedback.Status;
                _eventMessage = feedback.Message;
                Debug.LogError($"[DotArena] Guest connect failed: {ex}");
                await DisposeConnectionAsync();
                _localMatch = null;
            }
            finally
            {
                if (_pendingUiRequest == PendingUiRequest.Login)
                {
                    _pendingUiRequest = PendingUiRequest.None;
                }
            }
        }

        private void OnDisconnected(Exception? ex)
        {
            if (_ignoreDisconnectCallback)
            {
                return;
            }

            _callbackInbox.EnqueueDisconnected(ex?.Message);
        }

        private void BeginControlReconnect(string? disconnectMessage)
        {
            if (_controlReconnectInProgress)
            {
                return;
            }

            _controlReconnectInProgress = true;
            _multiplayerState.SessionController.MarkReconnecting();
            _status = string.IsNullOrWhiteSpace(disconnectMessage)
                ? "主连接已断开，正在重连"
                : $"主连接已断开，正在重连: {disconnectMessage}";
            _eventMessage = "正在恢复联机主连接";
            _ = ReconnectControlAsync();
        }

        private async Task ReconnectControlAsync()
        {
            const int maxAttempts = 5;
            var lastError = string.Empty;

            try
            {
                for (var attempt = 1; attempt <= maxAttempts && !_cts.IsCancellationRequested; attempt++)
                {
                    _status = $"主连接重连中 ({attempt}/{maxAttempts})";
                    _eventMessage = "正在恢复联机主连接";

                    try
                    {
                        var reply = await NetworkSession.ConnectAndLoginAsync(
                            _host,
                            _port,
                            _path,
                            _account,
                            _password,
                            guestLogin: false,
                            reconnect: true,
                            this,
                            _cts.Token);

                        if (reply.Code == LoginResultCodes.Ok)
                        {
                            var playerId = string.IsNullOrWhiteSpace(reply.PlayerId) ? _authenticatedPlayerId : reply.PlayerId;
                            _multiplayerState.ApplyControlReconnect(playerId, reply.Token, reply.SessionId, reply.SessionGeneration, reply.WinCount);
                            EnsureMetaState(_localPlayerId);
                            _ = RefreshLeaderboardAsync();
                            _status = _flowState == FrontendFlowState.InMatch
                                ? $"对局中: {_localPlayerId}"
                                : $"联机大厅: {_localPlayerId}";
                            _eventMessage = "主连接已恢复";

                            if (_lastRealtimeConnection != null && !NetworkSession.IsRealtimeConnected)
                            {
                                _ = EnsureRealtimeSessionAsync(_lastRealtimeConnection);
                            }

                            return;
                        }

                        if (reply.Code == LoginResultCodes.ReconnectStateLost)
                        {
                            _status = "联机状态已过期，正在重新登录";
                            _eventMessage = string.IsNullOrWhiteSpace(reply.Message)
                                ? "服务端会话已失效，正在启动新的连接"
                                : reply.Message;
                            await StartNewConnectionAfterStateLostAsync().ConfigureAwait(false);
                            return;
                        }

                        lastError = $"code={reply.Code}";
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        lastError = ex.Message;
                        Debug.LogWarning($"[DotArena] Control reconnect attempt {attempt} failed: {ex.Message}");
                    }

                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(1 << attempt, 8)), _cts.Token);
                }
            }
            finally
            {
                _controlReconnectInProgress = false;
            }

            ResetToModeSelect(
                status: string.IsNullOrWhiteSpace(lastError) ? "主连接重连失败" : $"主连接重连失败: {lastError}",
                eventMessage: "联机连接已断开，请重新登录",
                toastMessage: null);
        }

        private async Task StartNewConnectionAfterStateLostAsync()
        {
            await NetworkSession.DisposeRealtimeAsync().ConfigureAwait(false);
            _multiplayerState.MarkSessionStateLost();
            ResetSessionPresentation();
            _callbackInbox.Clear();
            _multiplayerState.ClearRequestState(resetReliablePush: false);
            _flowState = FrontendFlowState.Entry;
            _entryMenuState = EntryMenuState.MultiplayerAuth;
            _multiplayerState.ClearSession();
            _localMatch = null;
            await ConnectAsync().ConfigureAwait(false);
        }

        private Task ReturnToMainMenuAfterMatchAsync(bool preserveLoginState)
        {
            return ReturnToMainMenuAfterMatchAsync(preserveLoginState, _localPlayerId, true);
        }

        private Task ReturnToMainMenuAfterMatchAsync(bool preserveLoginState, string winnerPlayerId, bool localPlayerWon)
        {
            var sessionMode = _sessionMode;
            var localMass = GetLocalPlayerMassValue();
            var authenticatedProfile = _multiplayerState.CaptureAuthenticatedProfile();

            if (_sessionMode == SessionMode.Multiplayer)
            {
                _ = NetworkSession.DisposeRealtimeAsync();
                _multiplayerState.LastRealtimeConnection = null;
                _multiplayerState.MatchmakingStartedAt = -1f;
            }

            if (_sessionMode != SessionMode.Multiplayer)
            {
                ResetSessionPresentation();
                _multiplayerState.ClearSession();
                _localMatch = null;
            }
            else
            {
                ResetSessionPresentation();
            }

            _localMatch = null;
            _flowState = FrontendFlowState.Settlement;
            _entryMenuState = EntryMenuState.Hidden;
            _status = "对局结束";
            _eventMessage = "查看结算后可再来一局或返回大厅。";

            if (preserveLoginState)
            {
                _multiplayerState.RestoreAuthenticatedProfile(authenticatedProfile);
                _sessionMode = SessionMode.Multiplayer;
                _localPlayerId = authenticatedProfile.PlayerId;
            }
            else
            {
                _multiplayerState.ClearAuthenticatedProfile();
                _multiplayerState.ClearSession();
            }

            _settlementSummary = new MatchSettlementSummary
            {
                Title = preserveLoginState ? "联机结算" : "单机结算",
                Detail = DotArenaUiTextComposer.BuildSettlementDetail(sessionMode, localMass, _localWinCount, winnerPlayerId, localPlayerWon, _currentArenaMapVariant, _currentArenaRuleVariant),
                RewardSummary = DotArenaUiTextComposer.BuildSettlementRewardSummary(sessionMode, _lastRewardSummary),
                TaskSummary = DotArenaUiTextComposer.BuildSettlementTaskSummary(_metaState),
                NextStepSummary = DotArenaUiTextComposer.BuildSettlementNextStepSummary(sessionMode, _currentArenaMapVariant, _currentArenaRuleVariant),
                WinnerPlayerId = winnerPlayerId,
                LocalPlayerMass = localMass,
                LocalWinCount = _localWinCount,
                LocalPlayerWon = localPlayerWon,
                SessionMode = sessionMode
            };

            if (preserveLoginState)
            {
                _ = RefreshLeaderboardAsync();
            }

            if (_metaState != null)
            {
                _lastRewardSummary = DotArenaMetaProgression.ApplyMatchResult(
                    _metaState,
                    sessionMode,
                    winnerPlayerId,
                    preserveLoginState ? authenticatedProfile.PlayerId : "Player",
                    localMass);
                _settlementSummary.RewardSummary = DotArenaUiTextComposer.BuildSettlementRewardSummary(sessionMode, _lastRewardSummary);
                _settlementSummary.TaskSummary = DotArenaUiTextComposer.BuildSettlementTaskSummary(_metaState);
            }
            else
            {
                _lastRewardSummary = null;
            }

            return Task.CompletedTask;
        }

        private void BeginShutdown()
        {
            if (_shutdownStarted)
            {
                return;
            }

            _shutdownStarted = true;
            _cts.Cancel();
            _ = DisposeConnectionAsync();
        }

        private async Task DisposeConnectionAsync(bool clearSessionState = true, bool logout = true)
        {
            _ignoreDisconnectCallback = true;
            try
            {
                await NetworkSession.DisposeAsync(logout).ConfigureAwait(false);
            }
            finally
            {
                _ignoreDisconnectCallback = false;
            }

            if (clearSessionState)
            {
                _multiplayerState.ClearSession();
                _localMatch = null;
            }
        }

        private async Task CancelMatchmakingAsync()
        {
            if (_flowState != FrontendFlowState.Matchmaking)
            {
                if (_pendingUiRequest == PendingUiRequest.CancelMatchmaking)
                {
                    _pendingUiRequest = PendingUiRequest.None;
                }
                return;
            }

            var preserveLoginState = _multiplayerState.HasRecoverableLogin;
            var authenticatedProfile = _multiplayerState.CaptureAuthenticatedProfile();

            if (_sessionMode == SessionMode.Multiplayer)
            {
                try
                {
                    await NetworkSession.DisposeRealtimeAsync().ConfigureAwait(false);
                    _multiplayerState.LastRealtimeConnection = null;
                    await NetworkSession.CancelMatchmakingAsync(_cts.Token).ConfigureAwait(false);
                    _status = "正在取消匹配";
                    _eventMessage = "等待服务器确认取消";
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[DotArena] Cancel matchmaking failed: {ex.Message}");
                    _pendingUiRequest = PendingUiRequest.None;
                    _flowState = FrontendFlowState.Matchmaking;
                    _entryMenuState = EntryMenuState.Hidden;
                    _status = $"取消匹配失败: {ex.Message}";
                    _eventMessage = "取消匹配失败，请重试";
                }

                return;
            }
            else
            {
                ResetSessionPresentation();
                _multiplayerState.ClearSession();
                _localMatch = null;
            }

            _localMatch = null;
            _multiplayerState.MatchmakingStartedAt = -1f;
            _flowState = FrontendFlowState.Entry;
            if (preserveLoginState)
            {
                _multiplayerState.RestoreAuthenticatedProfile(authenticatedProfile);
                _sessionMode = SessionMode.Multiplayer;
                _localPlayerId = authenticatedProfile.PlayerId;
                _entryMenuState = EntryMenuState.MultiplayerLobby;
                _status = $"联机大厅: {authenticatedProfile.PlayerId}";
                _eventMessage = "已返回联机大厅";
            }
            else
            {
                _multiplayerState.ClearAuthenticatedProfile();
                _multiplayerState.ClearSession();
                _entryMenuState = EntryMenuState.ModeSelect;
                _status = "选择模式";
                _eventMessage = "请选择单机或联机";
            }
        }

        private void ProcessMenuRequests()
        {
            if (IsConnecting)
            {
                return;
            }

            if (_flowState == FrontendFlowState.Settlement)
            {
                if (_returnToLobbyRequested)
                {
                    _returnToLobbyRequested = false;
                    _rematchRequested = false;
                    ReturnToEntryMenuFromSettlement();
                }

                if (_rematchRequested)
                {
                    _rematchRequested = false;
                    var sessionMode = _settlementSummary?.SessionMode ?? SessionMode.SinglePlayer;
                    _settlementSummary = null;
                    _flowState = FrontendFlowState.Entry;

                    if (sessionMode == SessionMode.SinglePlayer)
                    {
                        _requestedSinglePlayerMode = _currentSinglePlayerMode;
                        BeginSinglePlayerMatch();
                    }
                    else
                    {
                        BeginMultiplayerMatchmaking();
                    }
                }

                return;
            }

            if (HasActiveSession || !_singlePlayerStartRequested)
            {
                return;
            }

            _singlePlayerStartRequested = false;
            BeginSinglePlayerMatch();
        }

        private void BeginMultiplayerMatchmaking()
        {
            if (!_hasAuthenticatedProfile || string.IsNullOrWhiteSpace(_authenticatedPlayerId))
            {
                _flowState = FrontendFlowState.Entry;
                _entryMenuState = EntryMenuState.MultiplayerAuth;
                _status = "请先登录联机账号";
                _eventMessage = "输入账号密码后进入联机大厅";
                return;
            }

            _ = BeginMultiplayerMatchmakingAsync();
        }

        private async Task BeginMultiplayerMatchmakingAsync()
        {
            if (!IsConnected)
            {
                await ConnectAsync().ConfigureAwait(false);
            }

            if (!IsConnected)
            {
                return;
            }

            _sessionMode = SessionMode.Multiplayer;
            _localPlayerId = _authenticatedPlayerId;
            _localMatch = null;
            _flowState = FrontendFlowState.Matchmaking;
            _entryMenuState = EntryMenuState.Hidden;
            _status = $"排队中: {_localPlayerId}";
            _eventMessage = "正在请求服务器分房";
            _settlementSummary = null;
            _matchmakingStartedAt = Time.time;

            try
            {
                await NetworkSession.DisposeRealtimeAsync().ConfigureAwait(false);
                _lastRealtimeConnection = null;
                await NetworkSession.StartMatchmakingAsync(_cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DotArena] Start matchmaking failed: {ex.Message}");
                _matchmakingStartedAt = -1f;
                _flowState = FrontendFlowState.Entry;
                _entryMenuState = EntryMenuState.MultiplayerLobby;
                _status = $"开始匹配失败: {ex.Message}";
                _eventMessage = "无法开始匹配";
            }
        }

        private void ConfigureWindow()
        {
#if !UNITY_EDITOR && UNITY_STANDALONE_WIN
            Screen.SetResolution(WindowWidth, WindowHeight, FullScreenMode.Windowed);
#endif
        }

        private void InitializeConnectionMode()
        {
            _flowState = FrontendFlowState.Entry;
            _entryMenuState = EntryMenuState.ModeSelect;
            _status = "选择模式";
            _eventMessage = "请选择单机或联机";
        }

        private string GetMenuLoginStatusText()
        {
            return DotArenaUiTextComposer.BuildMenuLoginStatusText(_hasAuthenticatedProfile, _authenticatedPlayerId, _localWinCount);
        }

        private void ApplyLaunchOverrides()
        {
            var launchArguments = Rpc.RpcLaunchArguments.ReadCurrentProcess();
            launchArguments.ApplyTo(ref _host, ref _port, ref _path);
            launchArguments.ApplyCredentials(ref _account, ref _password);

            if (launchArguments.HasOverrides)
            {
                Debug.Log($"[LaunchArgs] DotArenaGame host={_host}, port={_port}, path={_path}, account={_account}");
            }
        }

        private void ReturnToEntryMenuFromSettlement()
        {
            var preserveLoginState = _settlementSummary?.SessionMode == SessionMode.Multiplayer;
            var authenticatedProfile = _multiplayerState.CaptureAuthenticatedProfile();

            _settlementSummary = null;
            _flowState = FrontendFlowState.Entry;

            if (preserveLoginState)
            {
                _multiplayerState.RestoreAuthenticatedProfile(authenticatedProfile);
                _sessionMode = SessionMode.Multiplayer;
                _localPlayerId = authenticatedProfile.PlayerId;
                _localMatch = null;
                _entryMenuState = EntryMenuState.MultiplayerLobby;
                _status = $"联机大厅: {authenticatedProfile.PlayerId}";
                _eventMessage = "已返回联机大厅";
                return;
            }

            _multiplayerState.ClearAuthenticatedProfile();
            _multiplayerState.ClearSession();
            _localMatch = null;
            _requestedSinglePlayerMode = SinglePlayerMode.Normal;
            _currentSinglePlayerMode = SinglePlayerMode.Normal;
            _entryMenuState = EntryMenuState.ModeSelect;
            _status = "选择模式";
            _eventMessage = "请选择单机或联机";
        }

        private void ResetToModeSelect(string status, string eventMessage, string? toastMessage, bool resetReliablePush = true)
        {
            _ = NetworkSession.DisposeRealtimeAsync();
            ResetSessionPresentation();
            _callbackInbox.Clear();
            _settlementSummary = null;
            _lastRewardSummary = null;
            _multiplayerState.ClearRequestState(resetReliablePush);
            _flowState = FrontendFlowState.Entry;
            _entryMenuState = EntryMenuState.ModeSelect;
            _multiplayerState.ClearAuthenticatedProfile();
            _multiplayerState.ClearSession();
            _localMatch = null;
            _requestedSinglePlayerMode = SinglePlayerMode.Normal;
            _currentSinglePlayerMode = SinglePlayerMode.Normal;
            _metaState = null;
            _status = status;
            _eventMessage = eventMessage;
            if (!string.IsNullOrWhiteSpace(toastMessage))
            {
                PushEvent(toastMessage!);
            }
        }
    }
}
