using System;

namespace Lakona.Game.Client.Sessions
{
    public sealed class ClientConnectionFailure
    {
        public ClientConnectionFailure(ClientConnectionFailureKind kind, string message)
        {
            Kind = kind;
            Message = string.IsNullOrWhiteSpace(message) ? kind.ToString() : message;
        }

        public ClientConnectionFailureKind Kind { get; }

        public string Message { get; }
    }

    public enum ClientConnectionFailureKind
    {
        ConnectFailed,
        HandshakeFailed,
        HeartbeatFailed,
        Disconnected
    }
}
