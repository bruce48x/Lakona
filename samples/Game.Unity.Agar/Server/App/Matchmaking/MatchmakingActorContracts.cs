using Server.App.Routing;
using Server.App.Sessions;
using MemoryPack;

namespace Server.App.Matchmaking;

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class MatchmakingStatusRequest
{
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class MatchmakingTickRequest
{
    [MemoryPackOrder(0)]
    public DateTime ObservedAtUtc { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class MatchmakingTimerStartRequest
{
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class MatchmakingTimerStopRequest
{
}

public sealed class MatchmakingState
{
    public int DefaultRoomSize { get; set; } = 10;

    public List<MatchmakingQueueTicket> PendingTickets { get; set; } = new();
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class MatchmakingStatusSnapshot
{
    [MemoryPackOrder(0)]
    public string QueueId { get; set; } = "";

    [MemoryPackOrder(1)]
    public int DefaultRoomSize { get; set; } = 10;

    [MemoryPackOrder(2)]
    public int QueuedCount { get; set; }

    [MemoryPackOrder(3)]
    public List<MatchmakingQueueTicket> PendingTickets { get; set; } = new();
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class MatchmakingEnqueueRequest
{
    [MemoryPackOrder(0)]
    public string UserId { get; set; } = "";

    [MemoryPackOrder(1)]
    public string SessionToken { get; set; } = "";

    [MemoryPackOrder(2)]
    public DateTime EnqueuedAtUtc { get; set; }

    [MemoryPackOrder(3)]
    public int Priority { get; set; }

    [MemoryPackOrder(4)]
    public string ControlSessionId { get; set; } = "";

}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class MatchmakingCancelRequest
{
    [MemoryPackOrder(0)]
    public string UserId { get; set; } = "";

    [MemoryPackOrder(1)]
    public string TicketId { get; set; } = "";

    [MemoryPackOrder(2)]
    public DateTime CancelledAtUtc { get; set; }

    [MemoryPackOrder(3)]
    public string Reason { get; set; } = "";
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class MatchmakingEnqueueResult
{
    [MemoryPackOrder(0)]
    public string UserId { get; set; } = "";

    [MemoryPackOrder(1)]
    public string TicketId { get; set; } = "";

    [MemoryPackOrder(2)]
    public bool Queued { get; set; }

    [MemoryPackOrder(3)]
    public bool Matched { get; set; }

    [MemoryPackOrder(4)]
    public int QueuePosition { get; set; } = -1;

    [MemoryPackOrder(5)]
    public string Message { get; set; } = "";

    [MemoryPackOrder(6)]
    public DateTime UpdatedAtUtc { get; set; }

    [MemoryPackOrder(7)]
    public RoomAssignment RoomAssignment { get; set; } = new();
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class MatchmakingCancelResult
{
    [MemoryPackOrder(0)]
    public string UserId { get; set; } = "";

    [MemoryPackOrder(1)]
    public string TicketId { get; set; } = "";

    [MemoryPackOrder(2)]
    public bool Cancelled { get; set; }

    [MemoryPackOrder(3)]
    public int QueuePosition { get; set; } = -1;

    [MemoryPackOrder(4)]
    public string Message { get; set; } = "";

    [MemoryPackOrder(5)]
    public DateTime UpdatedAtUtc { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class MatchmakingQueueTicket
{
    [MemoryPackOrder(0)]
    public string TicketId { get; set; } = "";

    [MemoryPackOrder(1)]
    public string UserId { get; set; } = "";

    [MemoryPackOrder(2)]
    public string SessionToken { get; set; } = "";

    [MemoryPackOrder(3)]
    public DateTime EnqueuedAtUtc { get; set; }

    [MemoryPackOrder(4)]
    public string QueueId { get; set; } = "";

    [MemoryPackOrder(5)]
    public int Priority { get; set; }

    [MemoryPackOrder(6)]
    public string ControlSessionId { get; set; } = "";

}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class RoomAssignment
{
    [MemoryPackOrder(0)]
    public string RoomId { get; set; } = "";

    [MemoryPackOrder(1)]
    public string MatchId { get; set; } = "";

    [MemoryPackOrder(2)]
    public DateTime AssignedAtUtc { get; set; }

    [MemoryPackOrder(3)]
    public int MaxPlayers { get; set; } = 10;

    [MemoryPackOrder(4)]
    public List<PlayerRoomAssignment> Players { get; set; } = new();

    [MemoryPackOrder(5)]
    public GatewayEndpointDescriptor RuntimeGateway { get; set; } = new();
}
