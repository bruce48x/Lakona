#nullable enable

using Shared.Interfaces;
using UnityEngine;

namespace Shared.Gameplay
{
    public sealed class ArenaFood
    {
        public PickupType Type { get; set; }
        public Vector2 Position { get; set; }
    }
}
