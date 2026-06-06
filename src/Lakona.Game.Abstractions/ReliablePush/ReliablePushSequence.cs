using System;

namespace Lakona.Game.Abstractions
{
    public readonly struct ReliablePushSequence : IEquatable<ReliablePushSequence>
    {
        private ReliablePushSequence(long value)
        {
            Value = value;
        }

        public long Value { get; }

        public static ReliablePushSequence From(long value)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Reliable push sequence must be positive.");
            }

            return new ReliablePushSequence(value);
        }

        public bool Equals(ReliablePushSequence other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object? obj)
        {
            return obj is ReliablePushSequence other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }
    }
}
