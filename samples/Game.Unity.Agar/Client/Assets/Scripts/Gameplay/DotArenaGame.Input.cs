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
            if (!HasActiveSession || Time.time < _nextInputAt)
            {
                return;
            }

            _nextInputAt = Time.time + InputSendIntervalSeconds;

            var move = ReadMoveVector();
            var inputSummary = $"{move.x:0.00},{move.y:0.00}";
            if (!string.Equals(_lastLoggedInputVector, inputSummary, StringComparison.Ordinal))
            {
                _lastLoggedInputVector = inputSummary;
                Debug.Log($"[DotArena] HandleInput mode={_sessionMode} move={inputSummary} localMatch={_localMatch != null}");
            }

            if (SubmitSinglePlayerInput(move))
            {
                return;
            }

            if (!CanSubmitGameplayInput)
            {
                return;
            }

            _ = SendInputAsync(move);
        }

        private async Task SendInputAsync(Vector2 move)
        {
            try
            {
                await NetworkSession.SubmitInputAsync(new InputMessage
                {
                    PlayerId = _localPlayerId,
                    MoveX = move.x,
                    MoveY = move.y,
                    Tick = ++_inputTick
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
