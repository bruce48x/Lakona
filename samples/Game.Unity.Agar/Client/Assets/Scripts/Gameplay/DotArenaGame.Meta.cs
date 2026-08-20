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
                var localRank = 0;
                foreach (var entry in reply.Entries)
                {
                    if (!string.Equals(entry.PlayerId, _localPlayerId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    _localWinCount = Math.Max(0, entry.WinCount);
                    _localVictoryPoints = Math.Max(0, entry.VictoryPoints);
                    localRank = Math.Max(0, entry.Rank);
                    break;
                }
                DotArenaMetaProgression.Save(_metaState);
                if (_stressMode)
                {
                    Debug.Log($"[Stress] Leaderboard refreshed entries={reply.Entries.Count}, localRank={localRank}, victoryPoints={_localVictoryPoints}, wins={_localWinCount}.");
                }
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
