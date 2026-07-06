#nullable enable

using System;
using System.Collections.Generic;

namespace SampleClient.Gameplay
{
    internal static class DotArenaUiTextComposer
    {
        public static string BuildSettlementDetail(SessionMode sessionMode, float localMass, int localWinCount, string winnerPlayerId, bool localPlayerWon, ArenaMapVariant mapVariant, ArenaRuleVariant ruleVariant)
        {
            var modeText = sessionMode == SessionMode.SinglePlayer ? "Single Player" : "Multiplayer";
            var resultText = localPlayerWon ? "Win" : "failed";
            var presetLabel = DotArenaSinglePlayerCatalog.GetPresetLabel(mapVariant, ruleVariant);
            var presetLine = sessionMode == SessionMode.SinglePlayer ? $"\nPreset: {presetLabel}" : string.Empty;
            return $"Mode: {modeText}{presetLine}\nResult: {resultText}\nWinner: {winnerPlayerId}\nMass: {DotArenaPresentation.FormatMass(localMass)}\nWins: {localWinCount}";
        }

        public static string BuildSettlementRewardSummary(SessionMode sessionMode, DotArenaRewardSummary? lastRewardSummary)
        {
            if (lastRewardSummary == null)
            {
                return sessionMode == SessionMode.Multiplayer
                    ? "Reward: syncing."
                    : "Reward: none this match.";
            }

            return $"Reward: XP +{lastRewardSummary.ExperienceGained}, coins +{lastRewardSummary.CurrencyGained}, level {lastRewardSummary.NewLevel}";
        }

        public static string BuildSettlementTaskSummary(DotArenaMetaState? metaState)
        {
            return string.Empty;
        }

        public static string BuildSettlementNextStepSummary(SessionMode sessionMode, ArenaMapVariant mapVariant, ArenaRuleVariant ruleVariant)
        {
            return sessionMode == SessionMode.Multiplayer
                ? "Next: return to the lobby and start matchmaking again."
                : $"Next: return to mode select, or replay {DotArenaSinglePlayerCatalog.GetPresetLabel(mapVariant, ruleVariant)}.";
        }

        public static string BuildMatchmakingDetail(SessionMode sessionMode, ArenaMapVariant mapVariant, ArenaRuleVariant ruleVariant, string status, string currentEventMessage, int elapsedSeconds, bool cancelRequestPending)
        {
            if (sessionMode == SessionMode.SinglePlayer)
            {
                return $"Preset: {DotArenaSinglePlayerCatalog.GetPresetLabel(mapVariant, ruleVariant)}\nCreating local match.";
            }

            var elapsedText = $"Waited {FormatElapsedSeconds(elapsedSeconds)}";
            if (cancelRequestPending)
            {
                return $"Canceling matchmaking\n{elapsedText}\nPlease wait, returning to lobby.";
            }

            if (status.Contains("success", StringComparison.Ordinal) ||
                currentEventMessage.Contains("entering game", StringComparison.Ordinal))
            {
                return $"Matched\n{elapsedText}\nEntering game.";
            }

            return $"Finding match\n{elapsedText}\nYou can cancel matchmaking at any time.";
        }

        private static string FormatElapsedSeconds(int elapsedSeconds)
        {
            elapsedSeconds = Math.Max(0, elapsedSeconds);
            var minutes = elapsedSeconds / 60;
            var seconds = elapsedSeconds % 60;
            return minutes > 0 ? $"{minutes}m {seconds:D2}s" : $"{seconds}s";
        }

        public static string GetRematchButtonLabel(SessionMode sessionMode)
        {
            return sessionMode == SessionMode.SinglePlayer ? "Play Again" : "Match Again";
        }

        private static string FormatDuration(int seconds)
        {
            var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
            if (span.TotalDays >= 1d)
            {
                return $"{(int)span.TotalDays}d {span.Hours}h ";
            }

            if (span.TotalHours >= 1d)
            {
                return $"{(int)span.TotalHours}h {span.Minutes}m ";
            }

            return $"{span.Minutes}m ";
        }

        private static string FormatBool(bool value)
        {
            return value ? "On" : "Off";
        }

        public static int GetMetaNextLevelRequirement(int level)
        {
            return 100 + ((Math.Max(1, level) - 1) * 25);
        }
    }
}
