using Agar.Sample.State.Contracts.Sessions;

namespace Agar.Sample.State.Contracts.Matchmaking;

public sealed class MatchmakingStatusRequest
{
}

public sealed class MatchmakingTickRequest
{
    public DateTime ObservedAtUtc { get; set; }
}

public sealed class MatchmakingTimerStartRequest
{
}

public sealed class MatchmakingTimerStopRequest
{
}

public sealed class MatchmakingState
{
    public int DefaultRoomSize { get; set; } = 10;

    public List<MatchmakingQueueTicket> PendingTickets { get; set; } = new();
}

public sealed class MatchmakingStatusSnapshot
{
    public string QueueId { get; set; } = "";

    public int DefaultRoomSize { get; set; } = 10;

    public int QueuedCount { get; set; }

    public List<MatchmakingQueueTicket> PendingTickets { get; set; } = new();
}

public sealed class MatchmakingEnqueueRequest
{
    public string UserId { get; set; } = "";

    public string SessionToken { get; set; } = "";

    public DateTime EnqueuedAtUtc { get; set; }

    public int Priority { get; set; }
}

public sealed class MatchmakingCancelRequest
{
    public string UserId { get; set; } = "";

    public string TicketId { get; set; } = "";

    public DateTime CancelledAtUtc { get; set; }

    public string Reason { get; set; } = "";
}

public sealed class MatchmakingEnqueueResult
{
    public string UserId { get; set; } = "";

    public string TicketId { get; set; } = "";

    public bool Queued { get; set; }

    public bool Matched { get; set; }

    public int QueuePosition { get; set; } = -1;

    public string Message { get; set; } = "";

    public DateTime UpdatedAtUtc { get; set; }

    public RoomAssignment RoomAssignment { get; set; } = new();
}

public sealed class MatchmakingCancelResult
{
    public string UserId { get; set; } = "";

    public string TicketId { get; set; } = "";

    public bool Cancelled { get; set; }

    public int QueuePosition { get; set; } = -1;

    public string Message { get; set; } = "";

    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class MatchmakingQueueTicket
{
    public string TicketId { get; set; } = "";

    public string UserId { get; set; } = "";

    public string SessionToken { get; set; } = "";

    public DateTime EnqueuedAtUtc { get; set; }

    public string QueueId { get; set; } = "";

    public int Priority { get; set; }
}

public sealed class RoomAssignment
{
    public string RoomId { get; set; } = "";

    public string MatchId { get; set; } = "";

    public DateTime AssignedAtUtc { get; set; }

    public int MaxPlayers { get; set; } = 10;

    public List<PlayerRoomAssignment> Players { get; set; } = new();

    public GatewayEndpointDescriptor RuntimeGateway { get; set; } = new();
}
