#nullable enable

using System;
using System.Collections.Generic;

namespace Lakona.Game.Abstractions.Sessions
{
    public sealed class GameClientHello
    {
        public int ProtocolVersionMin { get; set; } = 1;

        public int ProtocolVersionMax { get; set; } = 1;

        public string ClientRuntime { get; set; } = string.Empty;

        public string ClientRuntimeVersion { get; set; } = string.Empty;

        public string GameVersion { get; set; } = string.Empty;

        public string BuildId { get; set; } = string.Empty;

        public string Platform { get; set; } = string.Empty;

        public List<string> SupportedCapabilities { get; set; } = new List<string>();
    }

    public sealed class GameServerHello
    {
        public int SelectedProtocolVersion { get; set; }

        public string ServerNodeId { get; set; } = string.Empty;

        public string EndpointTransport { get; set; } = string.Empty;

        public string EndpointSerializer { get; set; } = string.Empty;

        public ReliablePushHandshakeSettings ReliablePush { get; set; } = new ReliablePushHandshakeSettings();

        public DateTimeOffset ServerTimeUtc { get; set; }

        public List<string> ServerCapabilities { get; set; } = new List<string>();
    }

    public sealed class ReliablePushHandshakeSettings
    {
        public bool Enabled { get; set; }

        public string DeliveryMode { get; set; } = "reliable";

        public bool AckRequired { get; set; }

        public bool ReplaySupported { get; set; }

        public int MaxPending { get; set; }
    }
}
