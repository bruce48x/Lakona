using System;
using Server.App.Routing;
using MemoryPack;

namespace Server.App.Sessions
{
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

    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class PlayerRealtimeClearRequest
    {
        [MemoryPackOrder(0)]
        public string UserId { get; set; } = "";

        [MemoryPackOrder(1)]
        public string RealtimeSessionId { get; set; } = "";

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

        [MemoryPackOrder(8)]
        public string ControlSessionId { get; set; } = "";

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

        [MemoryPackOrder(5)]
        public string RealtimeSessionId { get; set; } = "";

        [MemoryPackOrder(10)]
        public string MatchmakingTicketId { get; set; } = "";

        [MemoryPackOrder(11)]
        public string CurrentRoomId { get; set; } = "";

        [MemoryPackOrder(12)]
        public string CurrentMatchId { get; set; } = "";

        [MemoryPackOrder(13)]
        public int SeatIndex { get; set; } = -1;

        [MemoryPackOrder(21)]
        public GatewayEndpointDescriptor RuntimeGateway { get; set; } = new();
    }

    public sealed class PlayerSessionState
    {
        public string UserId { get; set; } = "";

        public string SessionToken { get; set; } = "";

        public string ConnectionId { get; set; } = "";

        public string ControlSessionId { get; set; } = "";

        public string RealtimeSessionId { get; set; } = "";

        public string MatchmakingTicketId { get; set; } = "";

        public string CurrentRoomId { get; set; } = "";

        public string CurrentMatchId { get; set; } = "";

        public int SeatIndex { get; set; } = -1;

        public GatewayEndpointDescriptor RuntimeGateway { get; set; } = new();
    }
}
