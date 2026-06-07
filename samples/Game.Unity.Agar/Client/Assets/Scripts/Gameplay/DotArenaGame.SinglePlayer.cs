#nullable enable

using UnityEngine;

namespace SampleClient.Gameplay
{
    public sealed partial class DotArenaGame
    {
        private readonly DotArenaSinglePlayerController _singlePlayerController = new();

        private void BeginSinglePlayerMatch()
        {
            var start = _singlePlayerController.BeginMatch(_requestedSinglePlayerMode, ref _singlePlayerPlaylistIndex);

            _settlementSummary = null;
            ResetSessionPresentation();
            _ = DisposeConnectionAsync(clearSessionState: false);
            _sessionMode = SessionMode.SinglePlayer;
            _flowState = FrontendFlowState.Matchmaking;
            _localPlayerId = start.LocalPlayerId;
            EnsureMetaState(_localPlayerId);
            _currentSinglePlayerMode = start.Mode;
            _currentArenaMapVariant = start.MapVariant;
            _currentArenaRuleVariant = start.RuleVariant;
            _localMatch = start.Match;
            _localWinCount = 0;
            _entryMenuState = EntryMenuState.Hidden;
            _status = $"{GetSinglePlayerModeLabel(_currentSinglePlayerMode)} | {DotArenaSinglePlayerCatalog.GetRuleVariantName(_currentArenaRuleVariant)}";
            _eventMessage = $"Loading {DotArenaSinglePlayerCatalog.GetMapVariantName(_currentArenaMapVariant)}";
            _lastWorldTick = -1;
            _inputTick = 0;
            Debug.Log("[DotArena] BeginSinglePlayerMatch");
            ApplyWorldState(start.InitialWorldState);
            PushEvent($"Preset: {DotArenaSinglePlayerCatalog.GetPresetLabel(_currentArenaMapVariant, _currentArenaRuleVariant)}", 4f);
            _status = $"{GetSinglePlayerModeLabel(_currentSinglePlayerMode)}: {_localPlayerId}";
        }

        private static string GetSinglePlayerModeLabel(SinglePlayerMode mode)
        {
            return mode == SinglePlayerMode.Invincible ? "单机：无敌模式" : "单机：普通模式";
        }

        private void TickLocalMatch()
        {
            if (_sessionMode != SessionMode.SinglePlayer || _singlePlayerController.Match == null)
            {
                return;
            }

            var result = _singlePlayerController.Tick(Time.deltaTime);
            foreach (var step in result.Steps)
            {
                ApplyWorldState(step.WorldState);

                foreach (var deadEvent in step.Deaths)
                {
                    HandleDeadEvent(deadEvent);
                }

                if (step.MatchEnd != null)
                {
                    HandleMatchEnd(step.MatchEnd);
                    break;
                }
            }
        }

        private bool SubmitSinglePlayerInput(Vector2 move)
        {
            if (_sessionMode != SessionMode.SinglePlayer || _singlePlayerController.Match == null)
            {
                return false;
            }

            _singlePlayerController.SubmitInput(move, ++_inputTick);
            return true;
        }

        private Vector2 ReadMoveVector()
        {
#if UNITY_EDITOR
            if (_hasEditorInputOverride)
            {
                return _editorMoveOverride.sqrMagnitude > 1f ? _editorMoveOverride.normalized : _editorMoveOverride;
            }
#endif

            var x = 0f;
            var y = 0f;

            if (DotArenaInputUtility.IsKeyPressed(KeyCode.A)) x -= 1f;
            if (DotArenaInputUtility.IsKeyPressed(KeyCode.D)) x += 1f;
            if (DotArenaInputUtility.IsKeyPressed(KeyCode.S)) y -= 1f;
            if (DotArenaInputUtility.IsKeyPressed(KeyCode.W)) y += 1f;

            var move = new Vector2(x, y);
            return move.sqrMagnitude > 1f ? move.normalized : move;
        }
    }
}
