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
        private string GetLocalPlayerMassText()
        {
            if (_localPlayerId.Length == 0)
            {
                return "0";
            }

            return _renderStates.TryGetValue(_localPlayerId, out var renderState)
                ? DotArenaPresentation.FormatMass(renderState.Mass)
                : "0";
        }

        private float GetLocalPlayerMassValue()
        {
            if (_localPlayerId.Length == 0)
            {
                return 0f;
            }

            return _renderStates.TryGetValue(_localPlayerId, out var renderState) ? renderState.Mass : 0f;
        }

        private string GetLocalPlayerBuffText()
        {
            if (_localPlayerId.Length == 0 || !_renderStates.TryGetValue(_localPlayerId, out var renderState))
            {
                return "mass 0";
            }

            return $"mass {DotArenaPresentation.FormatMass(renderState.Mass)} / speed {renderState.MoveSpeed:0.0}";
        }

        private string GetCurrentEventMessage()
        {
            if (_eventMessageUntil > 0f && Time.time > _eventMessageUntil)
            {
                _eventMessageUntil = 0f;
                _eventMessage = _views.Count < 2 ? "等待玩家加入" : "对局进行中";
            }

            return _eventMessage;
        }

        private int GetMatchmakingElapsedSeconds()
        {
            if (_flowState != FrontendFlowState.Matchmaking || _matchmakingStartedAt < 0f)
            {
                return 0;
            }

            return Math.Max(0, Mathf.FloorToInt(Time.time - _matchmakingStartedAt));
        }

        private void PushEvent(string message, float durationSeconds = 3f)
        {
            _eventMessage = message;
            _eventMessageUntil = Time.time + durationSeconds;
        }
    }
}
