#nullable enable

using System;

namespace Shared.Gameplay
{
    // Xorshift32 keeps the simulation seed and random stream identical across clients.
    internal sealed class DeterministicRandom
    {
        private const uint DefaultSeed = 0x6D2B79F5u;
        private uint _state;

        public DeterministicRandom(uint seed)
        {
            _state = seed == 0 ? DefaultSeed : seed;
        }

        public uint State => _state;

        public float NextSingle()
        {
            return (NextUInt32() >> 8) * (1f / 16777216f);
        }

        public int NextInt32(int exclusiveMaximum)
        {
            if (exclusiveMaximum <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum));
            }

            return (int)(NextUInt32() % (uint)exclusiveMaximum);
        }

        private uint NextUInt32()
        {
            var value = _state;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            _state = value;
            return value;
        }
    }
}
