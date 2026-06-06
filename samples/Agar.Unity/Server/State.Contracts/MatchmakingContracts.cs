using Agar.Sample.State.Contracts;
using Agar.Sample.State.Contracts.Sessions;
using Agar.Sample.State.Contracts.Rooms;

namespace Agar.Sample.State.Contracts.Matchmaking;

public interface IMatchmakingActor
{
    Task<MatchmakingEnqueueResult> EnqueueAsync(MatchmakingEnqueueRequest request);
    Task<MatchmakingCancelResult> CancelAsync(MatchmakingCancelRequest request);
    Task TickAsync(MatchmakingTickRequest request);
    Task<MatchmakingStatusSnapshot> GetStatusAsync();
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

public sealed class MatchmakingTickRequest
{
    public DateTime ObservedAtUtc { get; set; }
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

public sealed class MatchmakingStatusSnapshot
{
    public string QueueId { get; set; } = "";

    public int DefaultRoomSize { get; set; } = 10;

    public int QueuedCount { get; set; }

    public string LastMatchId { get; set; } = "";

    public string LastRoomId { get; set; } = "";

    public DateTime LastUpdatedAtUtc { get; set; }

    public List<MatchmakingQueueTicket> PendingTickets { get; set; } = [];
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

public sealed class MatchmakingState
{
    public string QueueId { get; set; } = "";

    public int DefaultRoomSize { get; set; } = 10;

    public List<MatchmakingQueueTicket> PendingTickets { get; set; } = [];

    public string LastMatchId { get; set; } = "";

    public string LastRoomId { get; set; } = "";

    public DateTime LastUpdatedAtUtc { get; set; }
}

public sealed class RoomAssignment
{
    public string RoomId { get; set; } = "";

    public string MatchId { get; set; } = "";

    public DateTime AssignedAtUtc { get; set; }

    public List<PlayerRoomAssignment> Players { get; set; } = [];

    public GatewayEndpointDescriptor RuntimeGateway { get; set; } = new();
}
