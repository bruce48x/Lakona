using MemoryPack;
using UnityEngine;

namespace Shared.Gameplay
{
    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class ArenaConfig
    {
        [MemoryPackOrder(0)]
        public Vector2 ArenaHalfExtents { get; set; } = new(50f, 50f);

        [MemoryPackOrder(1)]
        public float RespawnInset { get; set; } = 3f;

        [MemoryPackOrder(2)]
        public float PlayerCollisionRadius { get; set; } = 0.9f;

        [MemoryPackOrder(3)]
        public float PlayerVisualRadius { get; set; } = 1.28f;

        [MemoryPackOrder(4)]
        public float PickupCollisionRadius { get; set; } = 0.75f;

        [MemoryPackOrder(5)]
        public float PickupSpawnInset { get; set; } = 2f;

        public static ArenaConfig CreateDefault()
        {
            return new ArenaConfig();
        }
    }
}
