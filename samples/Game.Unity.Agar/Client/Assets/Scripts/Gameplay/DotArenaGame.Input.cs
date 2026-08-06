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
        private void HandleInput()
        {
            if (!HasActiveSession)
            {
                _pendingCheatMass = false;
                return;
            }

            if ((_localMatch != null || _flowState == FrontendFlowState.InMatch) &&
                DotArenaInputUtility.WasKeyPressedThisFrame(KeyCode.P))
            {
                _pendingCheatMass = true;
            }

            if (Time.time < _nextInputAt)
            {
                return;
            }

            _nextInputAt = Time.time + InputSendIntervalSeconds;

            var move = ReadMoveVector();
            var addCheatMass = _pendingCheatMass;
            var inputSummary = $"{move.x:0.00},{move.y:0.00}";
            if (!string.Equals(_lastLoggedInputVector, inputSummary, StringComparison.Ordinal))
            {
                _lastLoggedInputVector = inputSummary;
                Debug.Log($"[DotArena] HandleInput mode={_sessionMode} move={inputSummary} localMatch={_localMatch != null}");
            }

            if (SubmitSinglePlayerInput(move, addCheatMass))
            {
                _pendingCheatMass = false;
                return;
            }

            if (!CanSubmitGameplayInput)
            {
                return;
            }

            _pendingCheatMass = false;
            _ = SendInputAsync(move, addCheatMass);
        }

        private async Task SendInputAsync(Vector2 move, bool addCheatMass = false)
        {
            try
            {
                await NetworkSession.SubmitInputAsync(new InputMessage
                {
                    PlayerId = _localPlayerId,
                    MoveX = move.x,
                    MoveY = move.y,
                    LastReceivedServerTick = Math.Max(0, _lastWorldTick),
                    AddCheatMass = addCheatMass
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _status = $"Input failed: {ex.Message}";
            }
        }

    }
}
