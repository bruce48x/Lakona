using System;
namespace Lakona.Game.Abstractions.Sessions;

public sealed class GameClientHello
{
    public int ProtocolVersion { get; set; } = 1;

    public string? ResumeTicket { get; set; }
}

public sealed class GameServerHello
{
    public int SelectedProtocolVersion { get; set; }

    public ReliablePushHandshakeSettings ReliablePush { get; set; } = new();

    public GameSessionResumeHandshakeSettings SessionResume { get; set; } = new();

    public GameHeartbeatHandshakeSettings Heartbeat { get; set; } = new();

    public GameSessionRecoveryHandshakeResult Recovery { get; set; } = new();
}

public enum GameSessionRecoveryStatus
{
    NotRequested = 0,
    Resumed = 1,
    StateLost = 2,
    StateRefreshRequired = 3,
    Terminated = 4,
}

public sealed class GameSessionRecoveryHandshakeResult
{
    public GameSessionRecoveryStatus Status { get; set; }

    public string? Reason { get; set; }
}

public sealed class GameSessionEstablished
{
    public string SessionId { get; set; } = "";

    public long SessionGeneration { get; set; }

    public string ResumeTicket { get; set; } = "";
}

public sealed class GameSessionResumeHandshakeSettings
{
    public TimeSpan Window { get; set; } = TimeSpan.FromSeconds(60);
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
