using System;
using System.Collections.Generic;
using MemoryPack;
using Server.App.Routing;
using Server.App.Sessions;
using Shared.Gameplay;
using Shared.Interfaces;

namespace Server.App.Rooms
{
    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class RoomCreateRequest
    {
        [MemoryPackOrder(0)]
        public string RoomId { get; set; } = "";

        [MemoryPackOrder(1)]
        public string MatchId { get; set; } = "";

        [MemoryPackOrder(2)]
        public string CreatedByUserId { get; set; } = "";

        [MemoryPackOrder(3)]
        public DateTime CreatedAtUtc { get; set; }

        [MemoryPackOrder(4)]
        public List<PlayerRoomAssignment> Players { get; set; } = new();
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class RoomPlayerLeaveRequest
    {
        [MemoryPackOrder(0)]
        public string UserId { get; set; } = "";

        [MemoryPackOrder(1)]
        public string RoomId { get; set; } = "";

        [MemoryPackOrder(2)]
        public DateTime LeftAtUtc { get; set; }

        [MemoryPackOrder(3)]
        public string Reason { get; set; } = "";
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class RoomPlayerReadyRequest
    {
        [MemoryPackOrder(0)]
        public string UserId { get; set; } = "";

        [MemoryPackOrder(1)]
        public string RoomId { get; set; } = "";

        [MemoryPackOrder(2)]
        public bool IsReady { get; set; }

        [MemoryPackOrder(3)]
        public string RealtimeSessionId { get; set; } = "";

        [MemoryPackOrder(5)]
        public DateTime UpdatedAtUtc { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class RoomRealtimeClearRequest
    {
        [MemoryPackOrder(0)]
        public string UserId { get; set; } = "";

        [MemoryPackOrder(1)]
        public string RoomId { get; set; } = "";

        [MemoryPackOrder(2)]
        public string RealtimeSessionId { get; set; } = "";

        [MemoryPackOrder(4)]
        public DateTime ClearedAtUtc { get; set; }

        [MemoryPackOrder(5)]
        public string Reason { get; set; } = "";
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class RoomStartRequest
    {
        [MemoryPackOrder(0)]
        public string StartedByUserId { get; set; } = "";

        [MemoryPackOrder(1)]
        public string RoomId { get; set; } = "";

        [MemoryPackOrder(2)]
        public DateTime StartedAtUtc { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class RoomMatchCompletion
    {
        [MemoryPackOrder(0)]
        public string RoomId { get; set; } = "";

        [MemoryPackOrder(1)]
        public string SettlementId { get; set; } = "";

        [MemoryPackOrder(2)]
        public DateTime FinishedAtUtc { get; set; }

        [MemoryPackOrder(3)]
        public string WinnerUserId { get; set; } = "";

        [MemoryPackOrder(4)]
        public string Reason { get; set; } = "";

        [MemoryPackOrder(5)]
        public List<RoomSettlementEntry> Results { get; set; } = new();
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class RoomInputSubmitRequest
    {
        [MemoryPackOrder(0)]
        public string RoomId { get; set; } = "";

        [MemoryPackOrder(1)]
        public string UserId { get; set; } = "";

        [MemoryPackOrder(2)]
        public string RealtimeSessionId { get; set; } = "";

        [MemoryPackOrder(4)]
        public InputMessage Input { get; set; } = new();

        [MemoryPackOrder(5)]
        public DateTime SubmittedAtUtc { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class RoomMatchResultSubmitRequest
    {
        [MemoryPackOrder(0)]
        public string RoomId { get; set; } = "";

        [MemoryPackOrder(1)]
        public string UserId { get; set; } = "";

        [MemoryPackOrder(2)]
        public string RealtimeSessionId { get; set; } = "";

        [MemoryPackOrder(3)]
        public FrameSyncMatchResult Result { get; set; } = new();

        [MemoryPackOrder(4)]
        public DateTime SubmittedAtUtc { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class RoomSettlementEntry
    {
        [MemoryPackOrder(0)]
        public string UserId { get; set; } = "";

        [MemoryPackOrder(1)]
        public int Rank { get; set; }

        [MemoryPackOrder(2)]
        public int Mass { get; set; }

        [MemoryPackOrder(3)]
        public bool IsWinner { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class RoomSettlementResult
    {
        [MemoryPackOrder(0)]
        public string RoomId { get; set; } = "";

        [MemoryPackOrder(1)]
        public string SettlementId { get; set; } = "";

        [MemoryPackOrder(2)]
        public bool Succeeded { get; set; }

        [MemoryPackOrder(3)]
        public bool AlreadyApplied { get; set; }

        [MemoryPackOrder(4)]
        public string WinnerUserId { get; set; } = "";

        [MemoryPackOrder(5)]
        public string Message { get; set; } = "";

        [MemoryPackOrder(6)]
        public DateTime UpdatedAtUtc { get; set; }

        [MemoryPackOrder(7)]
        public RoomSnapshot Snapshot { get; set; } = new();
    }

    [MemoryPackable]
    public sealed partial class RoomSnapshot
    {
        public string RoomId { get; set; } = "";

        public string MatchId { get; set; } = "";

        public DateTime CreatedAtUtc { get; set; }

        public DateTime StartedAtUtc { get; set; }

        public DateTime FinishedAtUtc { get; set; }

        public long Revision { get; set; }

        public List<RoomPlayerSnapshot> Players { get; set; } = new();

        public string WinnerUserId { get; set; } = "";

        public string SettlementId { get; set; } = "";

        public DateTime LastUpdatedAtUtc { get; set; }

        public string Message { get; set; } = "";

        public int MemberCount { get; set; }

        public int ConnectedCount { get; set; }

        public int ReadyCount { get; set; }

        public int CapacityRemaining { get; set; }

        public GatewayEndpointDescriptor RuntimeGateway { get; set; } = new();
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class RoomPlayerSnapshot
    {
        [MemoryPackOrder(0)]
        public string UserId { get; set; } = "";

        [MemoryPackOrder(1)]
        public string SessionToken { get; set; } = "";

        [MemoryPackOrder(2)]
        public string ConnectionId { get; set; } = "";

        [MemoryPackOrder(3)]
        public string RealtimeSessionId { get; set; } = "";

        [MemoryPackOrder(5)]
        public int SeatIndex { get; set; } = -1;

        [MemoryPackOrder(6)]
        public bool IsReady { get; set; }

        [MemoryPackOrder(7)]
        public bool IsConnected { get; set; }

        [MemoryPackOrder(8)]
        public DateTime JoinedAtUtc { get; set; }

        [MemoryPackOrder(9)]
        public DateTime LastSeenAtUtc { get; set; }

        [MemoryPackOrder(10)]
        public DateTime LeftAtUtc { get; set; }

        [MemoryPackOrder(11)]
        public string LeaveReason { get; set; } = "";

        [MemoryPackOrder(12)]
        public int Rank { get; set; }

        [MemoryPackOrder(13)]
        public string ControlSessionId { get; set; } = "";

        [MemoryPackOrder(14)]
        public int LastReceivedServerTick { get; set; }

    }

    public sealed class RoomState
    {
        public string RoomId { get; set; } = "";

        public string MatchId { get; set; } = "";

        public RoomStatus Status { get; set; } = RoomStatus.Created;

        public DateTime CreatedAtUtc { get; set; }

        public DateTime StartedAtUtc { get; set; }

        public DateTime FinishedAtUtc { get; set; }

        public long Revision { get; set; }

        public List<RoomPlayerState> Players { get; set; } = new();

        public string WinnerUserId { get; set; } = "";

        public string SettlementId { get; set; } = "";

        public DateTime LastUpdatedAtUtc { get; set; }

        public string Message { get; set; } = "";

        public GatewayEndpointDescriptor RuntimeGateway { get; set; } = new();

        public FrameSyncStart? FrameSyncStart { get; set; }

        public List<FrameSyncFrame> FrameHistory { get; set; } = new();

        public int LastPublishedFrame { get; set; }

        public int LastPublishedProgressRemainingSeconds { get; set; } = -1;

        public long ProgressRevision { get; set; }

        public bool MatchCommitted { get; set; }
    }

    public sealed class RoomPlayerState
    {
        public string UserId { get; set; } = "";

        public string SessionToken { get; set; } = "";

        public string ConnectionId { get; set; } = "";

        public string RealtimeSessionId { get; set; } = "";

        public string ControlSessionId { get; set; } = "";

        public int SeatIndex { get; set; } = -1;

        public bool IsReady { get; set; }

        public bool IsConnected { get; set; }

        public DateTime JoinedAtUtc { get; set; }

        public DateTime LastSeenAtUtc { get; set; }

        public DateTime LeftAtUtc { get; set; }

        public string LeaveReason { get; set; } = "";

        public int Rank { get; set; }

        public float InputX { get; set; }

        public float InputY { get; set; }

        public int LastReceivedServerTick { get; set; }

        public bool PendingCheatMass { get; set; }
    }

    public enum RoomStatus
    {
        Created = 0,
        WaitingForPlayers = 1,
        InProgress = 2,
        Finished = 3,
        Cancelled = 4
    }
}
