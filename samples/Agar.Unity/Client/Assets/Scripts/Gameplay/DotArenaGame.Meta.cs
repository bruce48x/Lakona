#nullable enable

using System;
using System.Threading.Tasks;
using UnityEngine;

namespace SampleClient.Gameplay
{
    public sealed partial class DotArenaGame
    {
        private void EnsureMetaState(string playerId)
        {
            _metaState = DotArenaMetaProgression.LoadOrCreate(playerId);
        }

        private async Task RefreshLeaderboardAsync()
        {
            if (_metaState == null || !IsConnected)
            {
                return;
            }

            try
            {
                var reply = await NetworkSession.GetLeaderboardAsync(10, _cts.Token);
                DotArenaMetaProgression.ApplyLeaderboard(_metaState, reply);
                DotArenaMetaProgression.Save(_metaState);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DotArena] Leaderboard refresh failed: {ex.Message}");
            }
        }
    }
}
