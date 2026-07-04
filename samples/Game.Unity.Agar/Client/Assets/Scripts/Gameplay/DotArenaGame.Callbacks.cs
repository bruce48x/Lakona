#nullable enable

using System;
using System.Linq;
using Shared.Gameplay;
using Shared.Interfaces;
using UnityEngine;
using static SampleClient.Gameplay.DotArenaTuning;

namespace SampleClient.Gameplay
{
    public sealed partial class DotArenaGame
    {
        public void OnWorldState(WorldState worldState)
        {
            _callbackInbox.EnqueueWorldState(worldState);
        }

        public void OnPlayerDead(PlayerDead deadEvent)
        {
            _callbackInbox.EnqueuePlayerDead(deadEvent);
        }

        public void OnMatchEnd(MatchEnd matchEnd)
        {
            _callbackInbox.EnqueueMatchEnd(matchEnd);
        }

        public void OnMatchmakingStatus(MatchmakingStatusUpdate matchmakingStatus)
        {
            _callbackInbox.EnqueueMatchmakingStatus(matchmakingStatus);
        }

        private void CaptureInputIntent()
        {
            if (DotArenaInputUtility.IsKeyDown(KeyCode.P))
            {
                _showDebugPanel = !_showDebugPanel;
            }
        }

        private void ApplyPendingCallbacks()
        {
            var pending = _callbackInbox.Drain();

            if (pending.DisconnectedMessage != null)
            {
                HandleDisconnectedOnMainThread(pending.DisconnectedMessage);
                return;
            }

            if (pending.WorldState != null)
            {
                ApplyWorldState(pending.WorldState);
            }

            if (pending.RealtimeFallbackMessage != null)
            {
                HandleRealtimeFallbackOnMainThread(pending.RealtimeFallbackMessage);
            }

            foreach (var deadEvent in pending.Deaths)
            {
                HandleDeadEvent(deadEvent);
            }

            if (pending.MatchEnd != null)
            {
                HandleMatchEnd(pending.MatchEnd);
            }

            if (pending.MatchmakingStatus != null)
            {
                HandleMatchmakingStatus(pending.MatchmakingStatus);
            }
        }

        private void HandleDisconnectedOnMainThread(string? disconnectMessage)
        {
            if (_sessionMode == SessionMode.SinglePlayer)
            {
                Debug.LogWarning($"[DotArena] Ignored remote disconnect while running single-player: {disconnectMessage ?? "Disconnected"}");
                return;
            }

            if (_multiplayerState.HasRecoverableLogin && !_shutdownStarted)
            {
                BeginControlReconnect(disconnectMessage);
                return;
            }

            ResetToModeSelect(
                status: string.IsNullOrWhiteSpace(disconnectMessage) ? "Disconnected" : $"Disconnected: {disconnectMessage}",
                eventMessage: "Multiplayer connection disconnected",
                toastMessage: null);
            Debug.LogWarning($"[DotArena] {_status}");
        }

        private void HandleRealtimeFallbackOnMainThread(string message)
        {
            if (_sessionMode != SessionMode.Multiplayer || !IsConnected)
            {
                HandleDisconnectedOnMainThread(message);
                return;
            }

            PushEvent(message, 5f);
            Debug.LogWarning($"[DotArena] {message}");
        }

        private void ApplyWorldState(WorldState worldState)
        {
            if (_flowState == FrontendFlowState.Settlement)
            {
                return;
            }

            var previousRoundRemainingSeconds = _lastRoundRemainingSeconds;
            WorldSynchronizer.ApplyWorldState(
                worldState,
                _localPlayerId,
                ref _lastWorldTick,
                ref _lastRoundRemainingSeconds,
                ref _lastLoggedPlayerCount,
                ref _currentArenaHalfExtents);

            if (previousRoundRemainingSeconds > 0 &&
                worldState.RoundRemainingSeconds <= 0 &&
                worldState.Players.Count > 1)
            {
                HandleMatchEnd(new MatchEnd
                {
                    WinnerPlayerId = SelectWinnerFromWorldState(worldState),
                    Tick = worldState.Tick
                });
                return;
            }

            if (_sessionMode != SessionMode.None &&
                _flowState != FrontendFlowState.Settlement &&
                worldState.Players.Count > 0)
            {
                _matchmakingStartedAt = -1f;
                _flowState = FrontendFlowState.InMatch;
                _entryMenuState = EntryMenuState.Hidden;
                _status = _sessionMode == SessionMode.SinglePlayer
                    ? $"Single-player match: {_localPlayerId}"
                    : $"Multiplayer match: {_localPlayerId}";
            }
        }

        private void HandleDeadEvent(PlayerDead deadEvent)
        {
            if (_renderStates.TryGetValue(deadEvent.PlayerId, out var renderState))
            {
                renderState.Alive = false;
                renderState.State = PlayerLifeState.Dead;
            }

            if (_views.TryGetValue(deadEvent.PlayerId, out var view))
            {
                var radius = renderState?.Radius ?? GameplayConfig.PlayerVisualRadius;
                var cosmeticId = deadEvent.PlayerId == _localPlayerId ? GetLocalPresentationCosmeticId() : null;
                view.ApplyPresentation(DotArenaPresentation.ResolvePlayerColor(deadEvent.PlayerId, cosmeticId), PlayerLifeState.Dead, false, radius);
            }

            PushEvent(deadEvent.PlayerId == _localPlayerId
                ? "You were consumed"
                : $"{deadEvent.PlayerId} was consumed");
        }

        private void HandleMatchEnd(MatchEnd matchEnd)
        {
            if (_flowState == FrontendFlowState.Settlement)
            {
                return;
            }

            if (_sessionMode == SessionMode.Multiplayer &&
                string.Equals(matchEnd.WinnerPlayerId, _localPlayerId, StringComparison.Ordinal))
            {
                _localWinCount += 1;
            }

            PushEvent(matchEnd.WinnerPlayerId == _localPlayerId
                ? "Victory"
                : $"Winner: {matchEnd.WinnerPlayerId}");

            _ = ReturnToMainMenuAfterMatchAsync(
                _sessionMode == SessionMode.Multiplayer,
                matchEnd.WinnerPlayerId,
                string.Equals(matchEnd.WinnerPlayerId, _localPlayerId, StringComparison.Ordinal));
        }

        private static string SelectWinnerFromWorldState(WorldState worldState)
        {
            return worldState.Players
                .OrderByDescending(static player => player.Mass)
                .ThenBy(static player => player.PlayerId, StringComparer.Ordinal)
                .FirstOrDefault()?.PlayerId ?? string.Empty;
        }

        private void HandleMatchmakingStatus(MatchmakingStatusUpdate matchmakingStatus)
        {
            ApplyMatchmakingStatus(matchmakingStatus);
        }

        private void ApplyMatchmakingStatus(MatchmakingStatusUpdate matchmakingStatus)
        {
            _sessionMode = SessionMode.Multiplayer;
            _localPlayerId = string.IsNullOrWhiteSpace(_authenticatedPlayerId) ? _localPlayerId : _authenticatedPlayerId;
            if (_matchmakingStartedAt < 0f &&
                matchmakingStatus.State is MatchmakingState.Queued or MatchmakingState.Searching or MatchmakingState.Matched)
            {
                _matchmakingStartedAt = Time.time;
            }

            if (matchmakingStatus.State == MatchmakingState.Matched &&
                matchmakingStatus.RealtimeConnection is { Transport: RealtimeTransportKind.Kcp } realtimeConnection)
            {
                _lastRealtimeConnection = CloneRealtimeConnection(realtimeConnection);
                _status = "Room ready, connecting KCP";
                _eventMessage = $"Opening realtime connection {realtimeConnection.Host}:{realtimeConnection.Port}";
                _ = EnsureRealtimeSessionAsync(realtimeConnection);
            }

            var viewState = DotArenaMultiplayerFlow.BuildMatchmakingViewState(
                matchmakingStatus,
                _pendingUiRequest == PendingUiRequest.CancelMatchmaking);

            if (viewState.ClearPendingCancelRequest)
            {
                _pendingUiRequest = PendingUiRequest.None;
                if (matchmakingStatus.State is MatchmakingState.Canceled or MatchmakingState.Failed)
                {
                    _matchmakingStartedAt = -1f;
                }
            }

            _flowState = viewState.FlowState;
            _entryMenuState = viewState.EntryMenuState;
            _status = viewState.Status;
            _eventMessage = viewState.EventMessage;
        }

        private void HandleSessionStateLost(string? message)
        {
            _multiplayerState.MarkSessionStateLost();
            ResetToModeSelect(
                status: "Multiplayer state expired",
                eventMessage: string.IsNullOrWhiteSpace(message) ? "Log in again to start a new multiplayer session" : message,
                toastMessage: null,
                resetSessionState: false);
        }

        private async System.Threading.Tasks.Task EnsureRealtimeSessionAsync(RealtimeConnectionInfo realtimeConnection)
        {
            try
            {
                var connected = await NetworkSession
                    .EnsureRealtimeConnectedAsync(realtimeConnection, this, _cts.Token)
                    .ConfigureAwait(false);

                if (!connected)
                {
                    HandleRealtimeAttachFailure("KCP realtime attach failed");
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DotArena] Realtime connect failed: {ex}");
                HandleRealtimeAttachFailure(ex.Message);
            }
        }

        private void HandleRealtimeAttachFailure(string message)
        {
            if (NetworkSession.IsConnected)
            {
                _callbackInbox.EnqueueRealtimeFallback($"Realtime channel unavailable, continuing on control channel: {message}");
                return;
            }

            _callbackInbox.EnqueueDisconnected(message);
        }

        private static RealtimeConnectionInfo CloneRealtimeConnection(RealtimeConnectionInfo source)
        {
            return new RealtimeConnectionInfo
            {
                Transport = source.Transport,
                Host = source.Host,
                Port = source.Port,
                Path = source.Path,
                RoomId = source.RoomId,
                MatchId = source.MatchId,
                SessionToken = source.SessionToken
            };
        }
    }
}
