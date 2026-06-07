#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SampleClient.Gameplay
{
    [Serializable]
    internal sealed class DotArenaMetaState
    {
        public string PlayerId = "Guest";
        public int Level = 1;
        public int Experience;
        public int SoftCurrency;
        public int TotalWins;
        public int TotalMatches;
        public int CurrentLoginStreak;
        public string LastLoginDateIso = string.Empty;
        public bool ClaimedFirstWinToday;
        public string LastFirstWinClaimDateIso = string.Empty;
        public string EquippedCosmeticId = "skin_default";
        public List<string> OwnedCosmeticIds = new() { "skin_default" };
        public List<DotArenaMatchRecord> MatchHistory = new();
        public string LeaderboardPeriodStartUtc = string.Empty;
        public int LeaderboardSecondsUntilReset;
        public List<DotArenaLeaderboardEntrySummary> LeaderboardEntries = new();
        public DotArenaSettings Settings = new();
    }

    [Serializable]
    internal sealed class DotArenaMatchRecord
    {
        public string Mode = string.Empty;
        public string Result = string.Empty;
        public int Mass;
        public string WinnerPlayerId = string.Empty;
        public string PlayedAtUtcIso = string.Empty;
    }

    [Serializable]
    internal sealed class DotArenaSettings
    {
        public float MasterVolume = 1f;
        public float MusicVolume = 0.8f;
        public float SfxVolume = 0.9f;
        public bool Fullscreen;
        public string Language = "zh-CN";
    }

    internal sealed class DotArenaRewardSummary
    {
        public int ExperienceGained { get; set; }
        public int CurrencyGained { get; set; }
        public bool ClaimedFirstWinReward { get; set; }
        public int NewLevel { get; set; }
    }

    internal sealed class DotArenaRecentMatchSummary
    {
        public bool HasRecord { get; set; }
        public string Mode { get; set; } = string.Empty;
        public string Result { get; set; } = string.Empty;
        public int Mass { get; set; }
        public string WinnerPlayerId { get; set; } = string.Empty;
        public string PlayedAtUtcIso { get; set; } = string.Empty;
    }

    internal sealed class DotArenaRecentMatchTrendSummary
    {
        public bool HasHistory { get; set; }
        public int SampleCount { get; set; }
        public int WinCount { get; set; }
        public int LossCount { get; set; }
        public float WinRate { get; set; }
        public int AverageMass { get; set; }
        public int BestMass { get; set; }
        public int CurrentStreak { get; set; }
        public string CurrentStreakType { get; set; } = string.Empty;
        public string TrendLabel { get; set; } = string.Empty;
        public string FormStrip { get; set; } = string.Empty;
    }

    internal sealed class DotArenaLeaderboardEntrySummary
    {
        public int Position { get; set; }
        public string Name { get; set; } = string.Empty;
        public int VictoryPoints { get; set; }
        public int Wins { get; set; }
        public int Matches { get; set; }
        public string Note { get; set; } = string.Empty;
        public bool IsLocalPlayer { get; set; }
    }

    internal sealed class DotArenaLeaderboardSummary
    {
        public bool HasProfile { get; set; }
        public string Title { get; set; } = string.Empty;
        public string PlayerLine { get; set; } = string.Empty;
        public string RankLine { get; set; } = string.Empty;
        public string TrendLine { get; set; } = string.Empty;
        public string FormLine { get; set; } = string.Empty;
        public List<DotArenaLeaderboardEntrySummary> Entries { get; set; } = new();
    }

    internal static partial class DotArenaMetaProgression
    {
    }
}
