#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Shared.Interfaces;
using UnityEngine;

namespace SampleClient.Gameplay
{
    internal static partial class DotArenaMetaProgression
    {
        public static bool TryGetRecentMatchSummary(DotArenaMetaState? state, out DotArenaRecentMatchSummary summary)
        {
            summary = GetRecentMatchSummary(state);
            return summary.HasRecord;
        }

        public static DotArenaRecentMatchSummary GetRecentMatchSummary(DotArenaMetaState? state)
        {
            if (state == null)
            {
                return new DotArenaRecentMatchSummary();
            }

            NormalizeState(state, state.PlayerId);
            if (state.MatchHistory.Count == 0)
            {
                return new DotArenaRecentMatchSummary();
            }

            var record = state.MatchHistory[0];
            return new DotArenaRecentMatchSummary
            {
                HasRecord = true,
                Mode = record.Mode,
                Result = record.Result,
                Mass = record.Mass,
                WinnerPlayerId = record.WinnerPlayerId,
                PlayedAtUtcIso = record.PlayedAtUtcIso
            };
        }

        public static DotArenaRecentMatchTrendSummary GetRecentMatchTrendSummary(DotArenaMetaState? state, int sampleCount = 5)
        {
            if (state == null)
            {
                return new DotArenaRecentMatchTrendSummary();
            }

            NormalizeState(state, state.PlayerId);
            if (state.MatchHistory.Count == 0)
            {
                return new DotArenaRecentMatchTrendSummary();
            }

            sampleCount = Math.Clamp(sampleCount, 1, 10);
            var historyCount = Math.Min(sampleCount, state.MatchHistory.Count);
            var wins = 0;
            var losses = 0;
            var massSum = 0;
            var bestMass = int.MinValue;
            var form = new List<string>(historyCount);

            for (var i = 0; i < historyCount; i++)
            {
                var record = state.MatchHistory[i];
                var won = string.Equals(record.Result, "Win", StringComparison.OrdinalIgnoreCase);
                form.Add(won ? "W" : "L");
                if (won)
                {
                    wins += 1;
                }
                else
                {
                    losses += 1;
                }

                massSum += record.Mass;
                if (record.Mass > bestMass)
                {
                    bestMass = record.Mass;
                }
            }

            var currentStreak = 0;
            var currentStreakType = string.Empty;
            var first = state.MatchHistory[0];
            var leadingWon = string.Equals(first.Result, "Win", StringComparison.OrdinalIgnoreCase);
            for (var i = 0; i < historyCount; i++)
            {
                var record = state.MatchHistory[i];
                var won = string.Equals(record.Result, "Win", StringComparison.OrdinalIgnoreCase);
                if (i == 0)
                {
                    currentStreakType = won ? "Win" : "Loss";
                }

                if (won != leadingWon)
                {
                    break;
                }

                currentStreak += 1;
            }

            var trendLabel = wins > losses
                ? "Hot"
                : losses > wins
                    ? "Cold"
                    : "Balanced";

            return new DotArenaRecentMatchTrendSummary
            {
                HasHistory = true,
                SampleCount = historyCount,
                WinCount = wins,
                LossCount = losses,
                WinRate = historyCount == 0 ? 0f : wins / (float)historyCount,
                AverageMass = historyCount == 0 ? 0 : Mathf.RoundToInt(massSum / (float)historyCount),
                BestMass = bestMass < 0 ? 0 : bestMass,
                CurrentStreak = currentStreak,
                CurrentStreakType = currentStreakType,
                TrendLabel = trendLabel,
                FormStrip = string.Join("-", form)
            };
        }

        public static DotArenaLeaderboardSummary GetLeaderboardSummary(DotArenaMetaState? state)
        {
            if (state == null)
            {
                return new DotArenaLeaderboardSummary();
            }

            NormalizeState(state, state.PlayerId);

            var trend = GetRecentMatchTrendSummary(state, 5);
            var totalMatches = Math.Max(0, state.TotalMatches);
            var winRate = totalMatches == 0 ? 0f : state.TotalWins / (float)totalMatches;
            var entries = new List<DotArenaLeaderboardEntrySummary>(state.LeaderboardEntries);
            var localEntry = entries.Find(entry => entry.IsLocalPlayer);
            var rankLine = localEntry != null
                ? $"Global rank: #{localEntry.Position} | VP: {localEntry.VictoryPoints} | Trend: {trend.TrendLabel}"
                : $"Global rank: Unranked | Win rate: {winRate:P0} | Trend: {trend.TrendLabel}";
            var resetLine = state.LeaderboardSecondsUntilReset > 0
                ? $"Weekly reset in {FormatLeaderboardReset(state.LeaderboardSecondsUntilReset)}"
                : "Weekly reset pending next server query";

            return new DotArenaLeaderboardSummary
            {
                HasProfile = true,
                Title = string.IsNullOrWhiteSpace(state.LeaderboardPeriodStartUtc)
                    ? "Global Leaderboard"
                    : $"Global Leaderboard ({state.LeaderboardPeriodStartUtc})",
                PlayerLine = $"Player: {state.PlayerId} | Wins: {state.TotalWins} | Matches: {state.TotalMatches} | Win rate: {winRate:P0}",
                RankLine = rankLine,
                TrendLine = trend.HasHistory
                    ? $"Recent form: {trend.FormStrip} | {trend.CurrentStreakType} streak: {trend.CurrentStreak} | Avg mass: {trend.AverageMass}"
                    : "Recent form: No history",
                FormLine = resetLine + " | " + (trend.HasHistory
                    ? $"Last {trend.SampleCount}: {trend.WinCount}W / {trend.LossCount}L | Best mass: {trend.BestMass}"
                    : "Last 0: no matches yet"),
                Entries = entries
            };
        }

        public static void ApplyLeaderboard(DotArenaMetaState? state, LeaderboardReply reply)
        {
            if (state == null || reply.Code != 0)
            {
                return;
            }

            NormalizeState(state, state.PlayerId);
            state.LeaderboardPeriodStartUtc = reply.PeriodStartUtc;
            state.LeaderboardSecondsUntilReset = Math.Max(0, reply.SecondsUntilReset);
            state.LeaderboardEntries.Clear();
            foreach (var entry in reply.Entries)
            {
                state.LeaderboardEntries.Add(new DotArenaLeaderboardEntrySummary
                {
                    Position = entry.Rank,
                    Name = entry.PlayerId,
                    VictoryPoints = entry.VictoryPoints,
                    Wins = entry.WinCount,
                    Matches = 0,
                    Note = "Server",
                    IsLocalPlayer = string.Equals(entry.PlayerId, state.PlayerId, StringComparison.Ordinal)
                });
            }
        }

        private static string FormatLeaderboardReset(int seconds)
        {
            var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
            if (span.TotalDays >= 1d)
            {
                return $"{(int)span.TotalDays}d {span.Hours}h";
            }

            if (span.TotalHours >= 1d)
            {
                return $"{(int)span.TotalHours}h {span.Minutes}m";
            }

            return $"{span.Minutes}m";
        }
    }
}
