using System;
using System.Collections.Generic;

namespace Lakona.Game.Abstractions.Sessions;

public sealed class GameClientHello
{
    public int ProtocolVersionMin { get; set; } = 1;

    public int ProtocolVersionMax { get; set; } = 1;

    public string ClientRuntime { get; set; } = "";

    public string ClientRuntimeVersion { get; set; } = "";

    public string GameVersion { get; set; } = "";

    public string BuildId { get; set; } = "";

    public string Platform { get; set; } = "";

    public List<string> SupportedCapabilities { get; set; } = new();
}

public sealed class GameServerHello
{
    public int SelectedProtocolVersion { get; set; }

    public string ServerNodeId { get; set; } = "";

    public string EndpointTransport { get; set; } = "";

    public string EndpointSerializer { get; set; } = "";

    public ReliablePushHandshakeSettings ReliablePush { get; set; } = new();

    public DateTimeOffset ServerTimeUtc { get; set; }

    public List<string> ServerCapabilities { get; set; } = new();
}

public sealed class ReliablePushHandshakeSettings
{
    public bool Enabled { get; set; }

    public string DeliveryMode { get; set; } = "reliable";

    public bool AckRequired { get; set; }

    public bool ReplaySupported { get; set; }

    public int MaxPending { get; set; }
}
