#nullable enable

using System;
using UnityEngine;

namespace Shared.Gameplay
{
    public sealed class ArenaPlayer
    {
        public ArenaPlayer(string playerId, int spawnIndex, float mass, bool isBot, int botNumber)
        {
            PlayerId = playerId;
            SpawnIndex = spawnIndex;
            Position = new Vector2(0f, 0f);
            Mass = NormalizeMass(mass);
            Alive = true;
            IsBot = isBot;
            BotNumber = botNumber;
        }

        public string PlayerId { get; }
        public int SpawnIndex { get; }
        public bool IsBot { get; }
        public int BotNumber { get; }
        public Vector2 Position { get; set; }
        public Vector2 Velocity { get; set; }
        public Vector2 Input { get; set; }
        public bool Alive { get; set; }
        public float RespawnRemaining { get; set; }
        public int LastInputTick { get; set; }
        public float Mass { get; set; }
        public float Radius { get; set; }

        private static float NormalizeMass(float mass)
        {
            return float.IsNaN(mass) || float.IsInfinity(mass) ? 0f : MathF.Max(0f, mass);
        }
    }
}
