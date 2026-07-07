using System;
namespace Lakona.Game.Abstractions.Sessions;

public sealed class GameClientHello
{
    public int ProtocolVersion { get; set; } = 1;
}

public sealed class GameServerHello
{
    public int SelectedProtocolVersion { get; set; }

    public ReliablePushHandshakeSettings ReliablePush { get; set; } = new();

    public GameHeartbeatHandshakeSettings Heartbeat { get; set; } = new();
}

public sealed class ReliablePushHandshakeSettings
{
    public bool Enabled { get; set; }

    public bool AckRequired { get; set; }
}

public sealed class GameHeartbeatHandshakeSettings
{
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(15);

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(45);
}
