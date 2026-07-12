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
            _status = $"Connecting to {Rpc.WebSocketRpcClientFactory.BuildUrl(_host, _port, _path)}";
            _eventMessage = "Signing in to multiplayer";
            _multiplayerState.SessionController.MarkConnecting();

            try
            {
                var reply = await NetworkSession.ConnectAndLoginAsync(_host, _port, _path, _account, _password, guestLogin: false, this, _cts.Token);

                if (reply.Code != 0)
                {
                    _multiplayerState.ClearAuthenticatedProfile();
                    _multiplayerState.ClearSession();
                    _localMatch = null;
                    _status = $"Login failed, code={reply.Code}";
                    _eventMessage = "Login failed. Check account or password";
                    return;
                }

                var playerId = string.IsNullOrWhiteSpace(reply.PlayerId) ? _account : reply.PlayerId;
                _multiplayerState.ApplyMultiplayerLogin(playerId, reply.Token, NetworkSession.ControlSessionId, NetworkSession.ControlSessionGeneration, reply.WinCount);
                _localMatch = null;
                EnsureMetaState(_localPlayerId);
                _ = RefreshLeaderboardAsync();
                _flowState = FrontendFlowState.Entry;
                _entryMenuState = EntryMenuState.MultiplayerLobby;
                _status = $"Multiplayer lobby: {_localPlayerId}";
                _eventMessage = "Login succeeded. Start matchmaking to enter the queue";
                Debug.Log($"[DotArena] Connected as {_localPlayerId} -> {Rpc.WebSocketRpcClientFactory.BuildUrl(_host, _port, _path)}");
                PushEvent("Login succeeded. Start matchmaking from the multiplayer lobby");
            }
            catch (OperationCanceledException)
            {
                _flowState = FrontendFlowState.Entry;
                _entryMenuState = EntryMenuState.MultiplayerAuth;
                _status = "Connection canceled";
                _eventMessage = "Login canceled";
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
            _status = $"Connecting to {Rpc.WebSocketRpcClientFactory.BuildUrl(_host, _port, _path)}";
            _eventMessage = "Requesting guest account";
            _multiplayerState.SessionController.MarkConnecting();

            try
            {
                var reply = await NetworkSession.ConnectAndLoginAsync(_host, _port, _path, string.Empty, string.Empty, guestLogin: true, this, _cts.Token);

                if (reply.Code != 0)
                {
                    _multiplayerState.ClearAuthenticatedProfile();
                    _multiplayerState.ClearSession();
                    _localMatch = null;
                    _status = $"Guest login failed, code={reply.Code}";
                    _eventMessage = "Could not request guest account. Try again later";
                    return;
                }

                _account = string.IsNullOrWhiteSpace(reply.Account) ? reply.PlayerId : reply.Account;
                _password = reply.Password;
                var playerId = string.IsNullOrWhiteSpace(reply.PlayerId) ? _account : reply.PlayerId;
                _multiplayerState.ApplyMultiplayerLogin(playerId, reply.Token, NetworkSession.ControlSessionId, NetworkSession.ControlSessionGeneration, reply.WinCount);
                _localMatch = null;
                EnsureMetaState(_localPlayerId);
                _ = RefreshLeaderboardAsync();
                _flowState = FrontendFlowState.Entry;
                _entryMenuState = EntryMenuState.MultiplayerLobby;
                _status = $"Multiplayer lobby: {_localPlayerId}";
                _eventMessage = "Guest login succeeded. Start matchmaking to enter the queue";
                Debug.Log($"[DotArena] Connected as guest {_localPlayerId} -> {Rpc.WebSocketRpcClientFactory.BuildUrl(_host, _port, _path)}");
                PushEvent("Guest login succeeded. Start matchmaking from the multiplayer lobby");
            }
            catch (OperationCanceledException)
            {
                _flowState = FrontendFlowState.Entry;
                _entryMenuState = EntryMenuState.MultiplayerAuth;
                _status = "Connection canceled";
                _eventMessage = "Guest login canceled";
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
            _status = "Match finished";
            _eventMessage = "Review the results, then play again or return to the lobby.";

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
                Title = preserveLoginState ? "Multiplayer results" : "Single-player results",
                Detail = DotArenaUiTextComposer.BuildSettlementDetail(sessionMode, localMass, _localWinCount, localPlayerWon, _currentArenaMapVariant, _currentArenaRuleVariant),
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

            var preserveLoginState = _multiplayerState.HasAuthenticatedMultiplayerProfile;
            var authenticatedProfile = _multiplayerState.CaptureAuthenticatedProfile();

            if (_sessionMode == SessionMode.Multiplayer)
            {
                try
                {
                    await NetworkSession.DisposeRealtimeAsync().ConfigureAwait(false);
                    _multiplayerState.LastRealtimeConnection = null;
                    await NetworkSession.CancelMatchmakingAsync(_cts.Token).ConfigureAwait(false);
                    _status = "Canceling matchmaking";
                    _eventMessage = "Waiting for server cancellation confirmation";
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
                    _status = $"Cancel matchmaking failed: {ex.Message}";
                    _eventMessage = "Cancel matchmaking failed. Try again";
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
                _status = $"Multiplayer lobby: {authenticatedProfile.PlayerId}";
                _eventMessage = "Returned to multiplayer lobby";
            }
            else
            {
                _multiplayerState.ClearAuthenticatedProfile();
                _multiplayerState.ClearSession();
                _entryMenuState = EntryMenuState.ModeSelect;
                _status = "Select mode";
                _eventMessage = "Choose single-player or multiplayer";
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
                _status = "Log in to multiplayer first";
                _eventMessage = "Enter account and password to enter the multiplayer lobby";
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
            _status = $"Queued: {_localPlayerId}";
            _eventMessage = "Requesting a room from the server";
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
                _status = $"Start matchmaking failed: {ex.Message}";
                _eventMessage = "Unable to start matchmaking";
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
            _status = "Select mode";
            _eventMessage = "Choose single-player or multiplayer";
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
                _status = $"Multiplayer lobby: {authenticatedProfile.PlayerId}";
                _eventMessage = "Returned to multiplayer lobby";
                return;
            }

            _multiplayerState.ClearAuthenticatedProfile();
            _multiplayerState.ClearSession();
            _localMatch = null;
            _requestedSinglePlayerMode = SinglePlayerMode.Normal;
            _currentSinglePlayerMode = SinglePlayerMode.Normal;
            _entryMenuState = EntryMenuState.ModeSelect;
            _status = "Select mode";
            _eventMessage = "Choose single-player or multiplayer";
        }

        private void ResetToModeSelect(string status, string eventMessage, string? toastMessage, bool resetSessionState = true)
        {
            _ = NetworkSession.DisposeRealtimeAsync();
            ResetSessionPresentation();
            _callbackInbox.Clear();
            _settlementSummary = null;
            _lastRewardSummary = null;
            _multiplayerState.ClearRequestState(resetSessionState);
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
