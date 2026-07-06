using System;
using System.Collections.Generic;

namespace Lakona.Game.Abstractions.Sessions;

public sealed class GameClientHello
{
    public int ProtocolVersion { get; set; } = 1;
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
