using System;

namespace Lakona.Game.Abstractions
{
    public readonly struct GameEndpointName : IEquatable<GameEndpointName>
    {
        public static readonly GameEndpointName Default = new GameEndpointName("default");

        public static readonly GameEndpointName Control = new GameEndpointName("control");

        public static readonly GameEndpointName Realtime = new GameEndpointName("realtime");

        public GameEndpointName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Endpoint name is required.", nameof(value));
            }

            Value = value;
        }

        public string Value { get; }

        public bool Equals(GameEndpointName other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return obj is GameEndpointName other && Equals(other);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Value);
        }

        public override string ToString()
        {
            return Value;
        }

        public static implicit operator GameEndpointName(string value)
        {
            return new GameEndpointName(value);
        }

        public static bool operator ==(GameEndpointName left, GameEndpointName right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(GameEndpointName left, GameEndpointName right)
        {
            return !left.Equals(right);
        }
    }
}
