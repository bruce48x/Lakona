using System;
using Server.App.State.Contracts;
using MemoryPack;

namespace Server.App.State.Contracts.Sessions
{
    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class PlayerSessionReconnectRequest
    {
        [MemoryPackOrder(0)]
        public string UserId { get; set; } = "";

        [MemoryPackOrder(1)]
        public string SessionToken { get; set; } = "";

        [MemoryPackOrder(2)]
        public string ConnectionId { get; set; } = "";

        [MemoryPackOrder(3)]
        public string ControlSessionId { get; set; } = "";

        [MemoryPackOrder(4)]
        public long ControlSessionGeneration { get; set; }

        [MemoryPackOrder(5)]
        public string RealtimeSessionId { get; set; } = "";

        [MemoryPackOrder(6)]
        public long RealtimeSessionGeneration { get; set; }

        [MemoryPackOrder(7)]
        public DateTime ReconnectedAtUtc { get; set; }

        [MemoryPackOrder(8)]
        public GatewayEndpointDescriptor ControlGateway { get; set; } = new();
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class PlayerSessionAttachRequest
    {
        [MemoryPackOrder(0)]
        public string UserId { get; set; } = "";

        [MemoryPackOrder(1)]
        public string SessionToken { get; set; } = "";

        [MemoryPackOrder(2)]
        public string ConnectionId { get; set; } = "";

        [MemoryPackOrder(3)]
        public string ControlSessionId { get; set; } = "";

        [MemoryPackOrder(4)]
        public long ControlSessionGeneration { get; set; }

        [MemoryPackOrder(5)]
        public string RealtimeSessionId { get; set; } = "";

        [MemoryPackOrder(6)]
        public long RealtimeSessionGeneration { get; set; }

        [MemoryPackOrder(7)]
        public DateTime AttachedAtUtc { get; set; }

        [MemoryPackOrder(8)]
        public GatewayEndpointDescriptor ControlGateway { get; set; } = new();
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class PlayerRealtimeAttachRequest
    {
        [MemoryPackOrder(0)]
        public string UserId { get; set; } = "";

        [MemoryPackOrder(1)]
        public string SessionToken { get; set; } = "";

        [MemoryPackOrder(2)]
        public string RoomId { get; set; } = "";

        [MemoryPackOrder(3)]
        public string MatchId { get; set; } = "";

        [MemoryPackOrder(4)]
        public string RealtimeSessionId { get; set; } = "";

        [MemoryPackOrder(5)]
        public long RealtimeSessionGeneration { get; set; }

        [MemoryPackOrder(6)]
        public DateTime AttachedAtUtc { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class PlayerRealtimeClearRequest
    {
        [MemoryPackOrder(0)]
        public string UserId { get; set; } = "";

        [MemoryPackOrder(1)]
        public string RealtimeSessionId { get; set; } = "";

        [MemoryPackOrder(2)]
        public long RealtimeSessionGeneration { get; set; }

        [MemoryPackOrder(3)]
        public DateTime ClearedAtUtc { get; set; }

        [MemoryPackOrder(4)]
        public string Reason { get; set; } = "";
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class PlayerSessionQueueRequest
    {
        [MemoryPackOrder(0)]
        public string UserId { get; set; } = "";

        [MemoryPackOrder(1)]
        public string QueueId { get; set; } = "";

        [MemoryPackOrder(2)]
        public string TicketId { get; set; } = "";

        [MemoryPackOrder(3)]
        public DateTime QueuedAtUtc { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class PlayerSessionQueueClearRequest
    {
        [MemoryPackOrder(0)]
        public string UserId { get; set; } = "";

        [MemoryPackOrder(1)]
        public string QueueId { get; set; } = "";

        [MemoryPackOrder(2)]
        public string TicketId { get; set; } = "";

        [MemoryPackOrder(3)]
        public DateTime ClearedAtUtc { get; set; }

        [MemoryPackOrder(4)]
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

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class PlayerRoomClearRequest
    {
        [MemoryPackOrder(0)]
        public string UserId { get; set; } = "";

        [MemoryPackOrder(1)]
        public string RoomId { get; set; } = "";

        [MemoryPackOrder(2)]
        public DateTime ClearedAtUtc { get; set; }

        [MemoryPackOrder(3)]
        public string Reason { get; set; } = "";
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class PlayerSessionDisconnectRequest
    {
        [MemoryPackOrder(0)]
        public string UserId { get; set; } = "";

        [MemoryPackOrder(1)]
        public string ConnectionId { get; set; } = "";

        [MemoryPackOrder(2)]
        public DateTime DisconnectedAtUtc { get; set; }

        [MemoryPackOrder(3)]
        public string Reason { get; set; } = "";
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class PlayerSessionSnapshot
    {
        [MemoryPackOrder(0)]
        public string UserId { get; set; } = "";

        [MemoryPackOrder(1)]
        public string SessionToken { get; set; } = "";

        [MemoryPackOrder(2)]
        public string ConnectionId { get; set; } = "";

        [MemoryPackOrder(3)]
        public string ControlSessionId { get; set; } = "";

        [MemoryPackOrder(4)]
        public long ControlSessionGeneration { get; set; }

        [MemoryPackOrder(5)]
        public string RealtimeSessionId { get; set; } = "";

        [MemoryPackOrder(6)]
        public long RealtimeSessionGeneration { get; set; }

        [MemoryPackOrder(7)]
        public bool IsOnline { get; set; }

        [MemoryPackOrder(8)]
        public bool IsQueued { get; set; }

        [MemoryPackOrder(9)]
        public string QueueId { get; set; } = "";

        [MemoryPackOrder(10)]
        public string MatchmakingTicketId { get; set; } = "";

        [MemoryPackOrder(11)]
        public string CurrentRoomId { get; set; } = "";

        [MemoryPackOrder(12)]
        public string CurrentMatchId { get; set; } = "";

        [MemoryPackOrder(13)]
        public int SeatIndex { get; set; } = -1;

        [MemoryPackOrder(14)]
        public DateTime AttachedAtUtc { get; set; }

        [MemoryPackOrder(15)]
        public DateTime LastQueuedAtUtc { get; set; }

        [MemoryPackOrder(16)]
        public DateTime LastConnectedAtUtc { get; set; }

        [MemoryPackOrder(17)]
        public DateTime LastDisconnectedAtUtc { get; set; }

        [MemoryPackOrder(18)]
        public DateTime LastHeartbeatAtUtc { get; set; }

        [MemoryPackOrder(19)]
        public string ReconnectToken { get; set; } = "";

        [MemoryPackOrder(20)]
        public GatewayEndpointDescriptor ControlGateway { get; set; } = new();

        [MemoryPackOrder(21)]
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
