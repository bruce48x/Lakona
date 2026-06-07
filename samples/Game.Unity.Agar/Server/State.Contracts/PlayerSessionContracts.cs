using Agar.Sample.State.Contracts;

namespace Agar.Sample.State.Contracts.Sessions;

public interface IPlayerSessionActor
{
    Task<PlayerSessionSnapshot> AttachAsync(PlayerSessionAttachRequest request);
    Task<PlayerSessionSnapshot> ReconnectAsync(PlayerSessionReconnectRequest request);
    Task<PlayerSessionSnapshot> MarkQueuedAsync(PlayerSessionQueueRequest request);
    Task<PlayerSessionSnapshot> ClearQueueAsync(PlayerSessionQueueClearRequest request);
    Task<PlayerSessionSnapshot> AssignRoomAsync(PlayerRoomAssignment request);
    Task<PlayerSessionSnapshot> ClearRoomAsync(PlayerRoomClearRequest request);
    Task<PlayerSessionSnapshot> MarkDisconnectedAsync(PlayerSessionDisconnectRequest request);
    Task<PlayerSessionSnapshot> HeartbeatAsync(PlayerSessionHeartbeatRequest request);
    Task<PlayerSessionSnapshot> GetSnapshotAsync();
}

public sealed class PlayerSessionReconnectRequest
{
    public string UserId { get; set; } = "";

    public string SessionToken { get; set; } = "";

    public string ConnectionId { get; set; } = "";

    public DateTime ReconnectedAtUtc { get; set; }

    public GatewayEndpointDescriptor ControlGateway { get; set; } = new();
}

public sealed class PlayerSessionAttachRequest
{
    public string UserId { get; set; } = "";

    public string SessionToken { get; set; } = "";

    public string ConnectionId { get; set; } = "";

    public DateTime AttachedAtUtc { get; set; }

    public GatewayEndpointDescriptor ControlGateway { get; set; } = new();
}

public sealed class PlayerSessionQueueRequest
{
    public string UserId { get; set; } = "";

    public string QueueId { get; set; } = "";

    public string TicketId { get; set; } = "";

    public DateTime QueuedAtUtc { get; set; }
}

public sealed class PlayerSessionQueueClearRequest
{
    public string UserId { get; set; } = "";

    public string QueueId { get; set; } = "";

    public string TicketId { get; set; } = "";

    public DateTime ClearedAtUtc { get; set; }

    public string Reason { get; set; } = "";
}

public sealed class PlayerRoomAssignment
{
    public string UserId { get; set; } = "";

    public string RoomId { get; set; } = "";

    public string MatchId { get; set; } = "";

    public int SeatIndex { get; set; } = -1;

    public string SessionToken { get; set; } = "";

    public string ConnectionId { get; set; } = "";

    public DateTime AssignedAtUtc { get; set; }

    public GatewayEndpointDescriptor RuntimeGateway { get; set; } = new();
}

public sealed class PlayerRoomClearRequest
{
    public string UserId { get; set; } = "";

    public string RoomId { get; set; } = "";

    public DateTime ClearedAtUtc { get; set; }

    public string Reason { get; set; } = "";
}

public sealed class PlayerSessionDisconnectRequest
{
    public string UserId { get; set; } = "";

    public string ConnectionId { get; set; } = "";

    public DateTime DisconnectedAtUtc { get; set; }

    public string Reason { get; set; } = "";
}

public sealed class PlayerSessionHeartbeatRequest
{
    public string UserId { get; set; } = "";

    public DateTime ObservedAtUtc { get; set; }
}

public sealed class PlayerSessionSnapshot
{
    public string UserId { get; set; } = "";

    public string SessionToken { get; set; } = "";

    public string ConnectionId { get; set; } = "";

    public bool IsOnline { get; set; }

    public bool IsQueued { get; set; }

    public string QueueId { get; set; } = "";

    public string MatchmakingTicketId { get; set; } = "";

    public string CurrentRoomId { get; set; } = "";

    public string CurrentMatchId { get; set; } = "";

    public int SeatIndex { get; set; } = -1;

    public DateTime AttachedAtUtc { get; set; }

    public DateTime LastQueuedAtUtc { get; set; }

    public DateTime LastConnectedAtUtc { get; set; }

    public DateTime LastDisconnectedAtUtc { get; set; }

    public DateTime LastHeartbeatAtUtc { get; set; }

    public string ReconnectToken { get; set; } = "";

    public GatewayEndpointDescriptor ControlGateway { get; set; } = new();

    public GatewayEndpointDescriptor RuntimeGateway { get; set; } = new();
}

public sealed class PlayerSessionState
{
    public string UserId { get; set; } = "";

    public string SessionToken { get; set; } = "";

    public string ConnectionId { get; set; } = "";

    public bool IsOnline { get; set; }

    public bool IsQueued { get; set; }

    public string QueueId { get; set; } = "";

    public string MatchmakingTicketId { get; set; } = "";

    public string CurrentRoomId { get; set; } = "";

    public string CurrentMatchId { get; set; } = "";

    public int SeatIndex { get; set; } = -1;

    public DateTime AttachedAtUtc { get; set; }

    public DateTime LastQueuedAtUtc { get; set; }

    public DateTime LastConnectedAtUtc { get; set; }

    public DateTime LastDisconnectedAtUtc { get; set; }

    public DateTime LastHeartbeatAtUtc { get; set; }

    public string ReconnectToken { get; set; } = "";

    public GatewayEndpointDescriptor ControlGateway { get; set; } = new();

    public GatewayEndpointDescriptor RuntimeGateway { get; set; } = new();
}
