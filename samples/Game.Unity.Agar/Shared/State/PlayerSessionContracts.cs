using System;
using Agar.Sample.State.Contracts;
using MemoryPack;

namespace Agar.Sample.State.Contracts.Sessions
{
    public sealed class PlayerSessionReconnectRequest
    {
        public string UserId { get; set; } = "";

        public string SessionToken { get; set; } = "";

        public string ConnectionId { get; set; } = "";

        public string ControlSessionId { get; set; } = "";

        public long ControlSessionGeneration { get; set; }

        public string RealtimeSessionId { get; set; } = "";

        public long RealtimeSessionGeneration { get; set; }

        public DateTime ReconnectedAtUtc { get; set; }

        public GatewayEndpointDescriptor ControlGateway { get; set; } = new();
    }

    public sealed class PlayerSessionAttachRequest
    {
        public string UserId { get; set; } = "";

        public string SessionToken { get; set; } = "";

        public string ConnectionId { get; set; } = "";

        public string ControlSessionId { get; set; } = "";

        public long ControlSessionGeneration { get; set; }

        public string RealtimeSessionId { get; set; } = "";

        public long RealtimeSessionGeneration { get; set; }

        public DateTime AttachedAtUtc { get; set; }

        public GatewayEndpointDescriptor ControlGateway { get; set; } = new();
    }

    public sealed class PlayerRealtimeAttachRequest
    {
        public string UserId { get; set; } = "";

        public string SessionToken { get; set; } = "";

        public string RoomId { get; set; } = "";

        public string MatchId { get; set; } = "";

        public string RealtimeSessionId { get; set; } = "";

        public long RealtimeSessionGeneration { get; set; }

        public DateTime AttachedAtUtc { get; set; }
    }

    public sealed class PlayerRealtimeClearRequest
    {
        public string UserId { get; set; } = "";

        public string RealtimeSessionId { get; set; } = "";

        public long RealtimeSessionGeneration { get; set; }

        public DateTime ClearedAtUtc { get; set; }

        public string Reason { get; set; } = "";
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

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class PlayerRoomAssignment
    {
        [MemoryPackOrder(0)]
        public string UserId { get; set; } = "";

        [MemoryPackOrder(1)]
        public string RoomId { get; set; } = "";

        [MemoryPackOrder(2)]
        public string MatchId { get; set; } = "";

        [MemoryPackOrder(3)]
        public int SeatIndex { get; set; } = -1;

        [MemoryPackOrder(4)]
        public string SessionToken { get; set; } = "";

        [MemoryPackOrder(5)]
        public string ConnectionId { get; set; } = "";

        [MemoryPackOrder(6)]
        public DateTime AssignedAtUtc { get; set; }

        [MemoryPackOrder(7)]
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

        public string ControlSessionId { get; set; } = "";

        public long ControlSessionGeneration { get; set; }

        public string RealtimeSessionId { get; set; } = "";

        public long RealtimeSessionGeneration { get; set; }

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

        public string ControlSessionId { get; set; } = "";

        public long ControlSessionGeneration { get; set; }

        public string RealtimeSessionId { get; set; } = "";

        public long RealtimeSessionGeneration { get; set; }

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
}
