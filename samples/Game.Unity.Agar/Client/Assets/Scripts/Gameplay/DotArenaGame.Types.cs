#nullable enable

using Shared.Interfaces;
using TMPro;
using UnityEngine;

namespace SampleClient.Gameplay
{
    internal sealed class PlayerRenderState
    {
        public Vector2 PreviousPosition { get; set; }
        public Vector2 TargetPosition { get; set; }
        public float ReceivedAt { get; set; }
        public PlayerLifeState State { get; set; }
        public bool Alive { get; set; }
        public float Mass { get; set; }
        public float Radius { get; set; }
        public float MoveSpeed { get; set; }
    }

    internal sealed class PlayerOverlayView
    {
        public PlayerOverlayView(GameObject root, RectTransform rootRect, TextMeshProUGUI nameText, TextMeshProUGUI massText)
        {
            Root = root;
            RootRect = rootRect;
            NameText = nameText;
            MassText = massText;
        }

        public GameObject Root { get; }
        public RectTransform RootRect { get; }
        public TextMeshProUGUI NameText { get; }
        public TextMeshProUGUI MassText { get; }
    }

    internal enum EntryMenuState
    {
        Hidden = 0,
        ModeSelect = 1,
        MultiplayerAuth = 2,
        MultiplayerLobby = 3
    }

    internal enum SessionMode
    {
        None = 0,
        SinglePlayer = 1,
        Multiplayer = 2
    }

    internal enum SinglePlayerMode
    {
        Normal = 0,
        Invincible = 1
    }

    internal enum FrontendFlowState
    {
        Entry = 0,
        Matchmaking = 1,
        InMatch = 2,
        Settlement = 3
    }

    internal sealed class MatchSettlementSummary
    {
        public string Title { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public string RewardSummary { get; set; } = string.Empty;
        public string TaskSummary { get; set; } = string.Empty;
        public string NextStepSummary { get; set; } = string.Empty;
        public string WinnerPlayerId { get; set; } = string.Empty;
        public float LocalPlayerMass { get; set; }
        public int LocalWinCount { get; set; }
        public bool LocalPlayerWon { get; set; }
        public SessionMode SessionMode { get; set; }
    }

    internal enum ArenaMapVariant
    {
        ClassicSquare = 0,
        NarrowBridge = 1,
        FinalRing = 2
    }

    internal enum ArenaRuleVariant
    {
        ClassicElimination = 0,
        MassRush = 1,
        ArenaCollapse = 2
    }

    internal readonly struct SinglePlayerMatchPreset
    {
        public SinglePlayerMatchPreset(ArenaMapVariant mapVariant, ArenaRuleVariant ruleVariant)
        {
            MapVariant = mapVariant;
            RuleVariant = ruleVariant;
        }

        public ArenaMapVariant MapVariant { get; }
        public ArenaRuleVariant RuleVariant { get; }
    }

    internal enum MetaTab
    {
        Lobby = 0,
        Tasks = 1,
        Shop = 2,
        Records = 3,
        Leaderboard = 4,
        Settings = 5
    }

    internal enum PendingUiRequest
    {
        None = 0,
        Login = 1,
        CancelMatchmaking = 2,
        ExitLobby = 3
    }
}
