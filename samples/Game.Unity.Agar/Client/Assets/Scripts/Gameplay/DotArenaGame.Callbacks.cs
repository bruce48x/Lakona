#nullable enable

using System;
using System.Linq;
using System.Threading.Tasks;
using Shared.Gameplay;
using Shared.Interfaces;
using Lakona.Game.Abstractions;
using Lakona.Game.Client.ReliablePush;
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
                status: string.IsNullOrWhiteSpace(disconnectMessage) ? "已断开连接" : $"已断开连接: {disconnectMessage}",
                eventMessage: "联机连接已断开",
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
                    ? $"单机对局: {_localPlayerId}"
                    : $"联机对局: {_localPlayerId}";
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
                ? "你被吞噬了"
                : $"{deadEvent.PlayerId} 被吞噬");
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
                ? "本局胜利"
                : $"胜者: {matchEnd.WinnerPlayerId}");

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
            if (matchmakingStatus.ReliableSequence <= 0)
            {
                ApplyMatchmakingStatus(matchmakingStatus);
                return;
            }

            _ = ProcessReliableMatchmakingStatusAsync(matchmakingStatus);
        }

        private async Task ProcessReliableMatchmakingStatusAsync(MatchmakingStatusUpdate matchmakingStatus)
        {
            try
            {
                var result = await _multiplayerState.ReliablePushInbox.ProcessAsync(
                    ReliablePushSequence.From(matchmakingStatus.ReliableSequence),
                    matchmakingStatus,
                    (payload, _) =>
                    {
                        ApplyMatchmakingStatus(payload);
                        return default;
                    },
                    async (ack, _) => await AckReliablePushAsync(ack.Sequence.Value),
                    _cts.Token);

                if (result.Acknowledgement is { Status: ReliablePushAckStatus.StateLost or ReliablePushAckStatus.SessionMismatch } acknowledgement)
                {
                    _multiplayerState.SessionController.ApplyAckOutcome(acknowledgement);
                    HandleSessionStateLost(acknowledgement.Reason);
                    return;
                }

                if (result.Acknowledgement is { } acknowledgementResult)
                {
                    _multiplayerState.SessionController.ApplyAckOutcome(acknowledgementResult);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (InvalidOperationException ex)
            {
                Debug.LogWarning($"[DotArena] Reliable push inbox is not ready: {ex.Message}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DotArena] Reliable matchmaking push failed: {ex.Message}");
            }
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
                _status = "房间已就绪，正在连接 KCP";
                _eventMessage = $"正在建立实时连接 {realtimeConnection.Host}:{realtimeConnection.Port}";
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

        private async Task<ReliablePushAckOutcome> AckReliablePushAsync(long sequence)
        {
            try
            {
                var reply = await NetworkSession.AckReliablePushAsync(sequence, _cts.Token);
                if (reply.RequiresNewSession)
                {
                    return ReliablePushAckOutcome.StateLost(reply.Message);
                }

                return reply.Code == ReliablePushAckResultCodes.Ok
                    ? ReliablePushAckOutcome.Accepted()
                    : ReliablePushAckOutcome.Duplicate();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DotArena] Reliable push ack failed: {ex.Message}");
                return ReliablePushAckOutcome.Duplicate();
            }
        }

        private void HandleSessionStateLost(string? message)
        {
            _multiplayerState.MarkSessionStateLost();
            ResetToModeSelect(
                status: "联机状态已过期",
                eventMessage: string.IsNullOrWhiteSpace(message) ? "请重新登录后开始新的联机会话" : message,
                toastMessage: null,
                resetReliablePush: false);
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
                _callbackInbox.EnqueueRealtimeFallback($"实时通道不可用，继续使用控制通道: {message}");
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
