namespace Lakona.Game.Abstractions.Sessions;

public sealed class GameHeartbeatRequest
{
    public int ProtocolVersion { get; set; } = 1;

    public string? SessionId { get; set; }
}

public sealed class GameHeartbeatReply
{
    public GameHeartbeatStatus Status { get; set; } = GameHeartbeatStatus.Ok;

    public string? Message { get; set; }
}

public enum GameHeartbeatStatus
{
    Ok,
    StateLost,
    Terminated
}
