#nullable enable

using System;
using Shared.Gameplay;
using Shared.Interfaces;
using UnityEngine;
using static SampleClient.Gameplay.DotArenaTuning;

namespace SampleClient.Gameplay
{
    public sealed partial class DotArenaGame
    {
        private readonly System.Collections.Generic.List<MatchProgressUpdate> _matchProgressHistory = new();

        public void OnFrameSyncStarted(FrameSyncStart start)
        {
            _callbackInbox.EnqueueFrameSyncStart(start);
        }

        public void OnFrame(FrameSyncFrame frame)
        {
            _callbackInbox.EnqueueFrame(frame);
        }

        public void OnMatchmakingStatus(MatchmakingStatusUpdate matchmakingStatus)
        {
            _callbackInbox.EnqueueMatchmakingStatus(matchmakingStatus);
        }

        public void OnMatchProgress(MatchProgressUpdate update)
        {
            _callbackInbox.EnqueueMatchProgress(update);
        }

        private void ApplyPendingCallbacks()
        {
            var pending = _callbackInbox.Drain();

            if (pending.DisconnectedMessage != null)
            {
                HandleDisconnectedOnMainThread(pending.DisconnectedMessage);
                return;
            }

            if (pending.FrameSyncStart != null)
            {
                BeginFrameSync(pending.FrameSyncStart);
            }

            if (pending.RealtimeFallbackMessage != null)
            {
                HandleRealtimeFallbackOnMainThread(pending.RealtimeFallbackMessage);
            }

            foreach (var frame in pending.Frames)
            {
                ApplyFrame(frame);
            }

            if (pending.MatchmakingStatus != null)
            {
                HandleMatchmakingStatus(pending.MatchmakingStatus);
            }

            foreach (var progress in pending.MatchProgress)
            {
                _matchProgressHistory.Add(progress);
            }
        }

        private void BeginFrameSync(FrameSyncStart start)
        {
            if (_frameSyncMatch != null &&
                string.Equals(_frameSyncMatch.MatchId, start.MatchId, StringComparison.Ordinal))
            {
                return;
            }

            _frameSyncMatch = new FrameSyncSimulation(start);
            _frameSyncResultReported = false;
            _inputTick = 0;
            ApplyWorldState(_frameSyncMatch.WorldState);
        }

        private void ApplyFrame(FrameSyncFrame frame)
        {
            if (_frameSyncMatch == null)
            {
                Debug.LogWarning($"[DotArena] Ignored frame {frame.Frame} before frame-sync start.");
                return;
            }

            var advance = _frameSyncMatch.SubmitFrame(frame);
            foreach (var step in advance.Steps)
            {
                ApplyWorldState(step.WorldState);
                foreach (var deadEvent in step.Deaths)
                {
                    HandleDeadEvent(deadEvent);
                }

                if (step.MatchEnd != null)
                {
                    _ = CompleteFrameSyncMatchAsync(step.WorldState, step.MatchEnd);
                    break;
                }
            }
        }

        private async System.Threading.Tasks.Task CompleteFrameSyncMatchAsync(
            WorldState worldState,
            MatchEnd matchEnd)
        {
            if (_sessionMode != SessionMode.Multiplayer ||
                _frameSyncMatch == null ||
                _frameSyncResultReported)
            {
                HandleMatchEnd(matchEnd);
                return;
            }

            _frameSyncResultReported = true;
            var settlement = MatchSettlementRules.Settle(worldState);
            var report = new FrameSyncMatchResult
            {
                RoomId = _frameSyncMatch.RoomId,
                MatchId = _frameSyncMatch.MatchId,
                Frame = worldState.Tick,
                WinnerPlayerId = settlement.WinnerPlayerId
            };
            foreach (var entry in settlement.Entries)
            {
                report.Players.Add(new FrameSyncPlayerResult
                {
                    PlayerId = entry.PlayerId,
                    Rank = entry.Rank,
                    Mass = entry.Mass,
                    IsWinner = entry.IsWinner
                });
            }

            try
            {
                await NetworkSession.SubmitMatchResultAsync(report);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DotArena] Match result submission failed: {ex}");
            }

            HandleMatchEnd(matchEnd);
        }

        private void HandleDisconnectedOnMainThread(string? disconnectMessage)
        {
            if (_sessionMode == SessionMode.SinglePlayer)
            {
                Debug.LogWarning($"[DotArena] Ignored remote disconnect while running single-player: {disconnectMessage ?? "Disconnected"}");
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

            WorldSynchronizer.ApplyWorldState(
                worldState,
                _localPlayerId,
                ref _lastWorldTick,
                ref _lastRoundRemainingSeconds,
                ref _lastLoggedPlayerCount,
                ref _currentArenaHalfExtents);

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
                : "A rival was consumed");
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
                : "Round complete");

            _ = ReturnToMainMenuAfterMatchAsync(
                _sessionMode == SessionMode.Multiplayer,
                matchEnd.WinnerPlayerId,
                string.Equals(matchEnd.WinnerPlayerId, _localPlayerId, StringComparison.Ordinal));
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
                _eventMessage = "Entering arena";
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
                var reply = await NetworkSession
                    .EnsureRealtimeConnectedAsync(realtimeConnection, this, _cts.Token)
                    .ConfigureAwait(false);

                if (reply == null)
                {
                    HandleRealtimeAttachFailure("KCP realtime attach failed");
                    return;
                }

                EnqueueRealtimeReplay(reply);
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

        private void RefreshRealtimeReplayAfterRecovery()
        {
            if (NetworkSession.ShouldRefreshRealtimeReplayAfterRecovery())
            {
                _ = RefreshRealtimeReplayAfterRecoveryAsync();
            }
        }

        private async System.Threading.Tasks.Task RefreshRealtimeReplayAfterRecoveryAsync()
        {
            try
            {
                var reply = await NetworkSession
                    .RefreshRealtimeReplayAfterRecoveryAsync()
                    .ConfigureAwait(false);
                if (reply == null)
                {
                    HandleRealtimeAttachFailure("KCP realtime replay refresh failed after recovery");
                    return;
                }

                EnqueueRealtimeReplay(reply);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DotArena] Realtime replay refresh failed: {ex}");
                HandleRealtimeAttachFailure(ex.Message);
            }
        }

        private void EnqueueRealtimeReplay(RealtimeAttachReply reply)
        {
            if (reply.FrameSyncStart != null)
            {
                _callbackInbox.EnqueueFrameSyncStart(reply.FrameSyncStart);
            }

            foreach (var frame in reply.ReplayFrames)
            {
                _callbackInbox.EnqueueFrame(frame);
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
