using System.Collections.Generic;
using Shared.Interfaces;

namespace Shared.Gameplay
{
    public sealed class ArenaSimulationState
    {
        public int Tick { get; set; }
        public float RoundElapsedSeconds { get; set; }
        public float CurrentArenaHalfExtentX { get; set; }
        public float CurrentArenaHalfExtentY { get; set; }
        public string WinnerPlayerId { get; set; } = "";
        public int RestartAtTick { get; set; } = -1;
        public int NextBotNumber { get; set; } = 1;
        public List<ArenaPlayerRuntimeState> Players { get; set; } = new();
        public List<ArenaFoodRuntimeState> Foods { get; set; } = new();
    }

    public sealed class ArenaPlayerRuntimeState
    {
        public string PlayerId { get; set; } = "";
        public int SpawnIndex { get; set; }
        public float Mass { get; set; }
        public bool IsBot { get; set; }
        public int BotNumber { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Vx { get; set; }
        public float Vy { get; set; }
        public float InputX { get; set; }
        public float InputY { get; set; }
        public int LastInputTick { get; set; }
        public bool Alive { get; set; } = true;
        public float RespawnRemaining { get; set; }
        public float Radius { get; set; }
    }

    public sealed class ArenaFoodRuntimeState
    {
        public PickupType Type { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
    }
}
