using System;

namespace Lakona.Game.Abstractions
{
    public readonly struct GameSessionKey : IEquatable<GameSessionKey>
    {
        public GameSessionKey(string ownerKey, string sessionId, long generation)
        {
            if (string.IsNullOrWhiteSpace(ownerKey))
            {
                throw new ArgumentException("Owner key is required.", nameof(ownerKey));
            }

            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new ArgumentException("Session id is required.", nameof(sessionId));
            }

            if (generation <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(generation), "Generation must be positive.");
            }

            OwnerKey = ownerKey;
            SessionId = sessionId;
            Generation = generation;
        }

        public string OwnerKey { get; }

        public string SessionId { get; }

        public long Generation { get; }

        public bool Equals(GameSessionKey other)
        {
            return string.Equals(OwnerKey, other.OwnerKey, StringComparison.Ordinal) &&
                string.Equals(SessionId, other.SessionId, StringComparison.Ordinal) &&
                Generation == other.Generation;
        }

        public override bool Equals(object? obj)
        {
            return obj is GameSessionKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = StringComparer.Ordinal.GetHashCode(OwnerKey);
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(SessionId);
                hash = (hash * 397) ^ Generation.GetHashCode();
                return hash;
            }
        }

        public override string ToString()
        {
            return OwnerKey + "/" + SessionId + "#" + Generation;
        }

        public static bool operator ==(GameSessionKey left, GameSessionKey right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(GameSessionKey left, GameSessionKey right)
        {
            return !left.Equals(right);
        }
    }
}
