using System;
using System.Collections.Generic;
using Server.App.State.Contracts;
using Server.App.State.Contracts.Sessions;
using Shared.Gameplay;
using Shared.Interfaces;

namespace Server.App.State.Contracts.Rooms
{
    public sealed class RoomCreateRequest
    {
        public string RoomId { get; set; } = "";

        public string MatchId { get; set; } = "";

        public string CreatedByUserId { get; set; } = "";

        public DateTime CreatedAtUtc { get; set; }

        public int MaxPlayers { get; set; } = 10;

        public List<PlayerRoomAssignment> Players { get; set; } = new();
    }

    public sealed class RoomPlayerLeaveRequest
    {
        public string UserId { get; set; } = "";

        public string RoomId { get; set; } = "";

        public DateTime LeftAtUtc { get; set; }

        public string Reason { get; set; } = "";
    }

    public sealed class RoomPlayerReadyRequest
    {
        public string UserId { get; set; } = "";

        public string RoomId { get; set; } = "";

        public bool IsReady { get; set; }

        public string RealtimeSessionId { get; set; } = "";

        public long RealtimeSessionGeneration { get; set; }

        public DateTime UpdatedAtUtc { get; set; }
    }

    public sealed class RoomRealtimeClearRequest
    {
        public string UserId { get; set; } = "";

        public string RoomId { get; set; } = "";

        public string RealtimeSessionId { get; set; } = "";

        public long RealtimeSessionGeneration { get; set; }

        public DateTime ClearedAtUtc { get; set; }

        public string Reason { get; set; } = "";
    }

    public sealed class RoomStartRequest
    {
        public string StartedByUserId { get; set; } = "";

        public string RoomId { get; set; } = "";

        public DateTime StartedAtUtc { get; set; }
    }

    public sealed class RoomMatchCompletion
    {
        public string RoomId { get; set; } = "";

        public string SettlementId { get; set; } = "";

        public DateTime FinishedAtUtc { get; set; }

        public string WinnerUserId { get; set; } = "";

        public string Reason { get; set; } = "";

        public List<RoomSettlementEntry> Results { get; set; } = new();
    }

    public sealed class RoomInputSubmitRequest
    {
        public string RoomId { get; set; } = "";

        public string UserId { get; set; } = "";

        public string RealtimeSessionId { get; set; } = "";

        public long RealtimeSessionGeneration { get; set; }

        public InputMessage Input { get; set; } = new();

        public DateTime SubmittedAtUtc { get; set; }
    }

    public sealed class RoomSettlementEntry
    {
        public string UserId { get; set; } = "";

        public int Rank { get; set; }

        public int Mass { get; set; }

        public bool IsWinner { get; set; }
    }

    public sealed class RoomSettlementResult
    {
        public string RoomId { get; set; } = "";

        public string SettlementId { get; set; } = "";

        public bool Succeeded { get; set; }

        public bool AlreadyApplied { get; set; }

        public string WinnerUserId { get; set; } = "";

        public string Message { get; set; } = "";

        public DateTime UpdatedAtUtc { get; set; }

        public RoomSnapshot Snapshot { get; set; } = new();
    }

    public sealed class RoomSnapshot
    {
        public string RoomId { get; set; } = "";

        public string MatchId { get; set; } = "";

        public RoomStatus Status { get; set; } = RoomStatus.Created;

        public int MaxPlayers { get; set; } = 10;

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

    public sealed class RoomPlayerSnapshot
    {
        public string UserId { get; set; } = "";

        public string SessionToken { get; set; } = "";

        public string ConnectionId { get; set; } = "";

        public string RealtimeSessionId { get; set; } = "";

        public long RealtimeSessionGeneration { get; set; }

        public int SeatIndex { get; set; } = -1;

        public bool IsReady { get; set; }

        public bool IsConnected { get; set; }

        public DateTime JoinedAtUtc { get; set; }

        public DateTime LastSeenAtUtc { get; set; }

        public DateTime LeftAtUtc { get; set; }

        public string LeaveReason { get; set; } = "";

        public int Rank { get; set; }
    }

    public sealed class RoomState
    {
        public string RoomId { get; set; } = "";

        public string MatchId { get; set; } = "";

        public RoomStatus Status { get; set; } = RoomStatus.Created;

        public int MaxPlayers { get; set; } = 10;

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

        public ArenaSimulationState Simulation { get; set; } = new();

        public WorldState LastWorldState { get; set; } = new();

        public int LastPublishedWorldTick { get; set; }

        public bool MatchCommitted { get; set; }
    }

    public sealed class RoomPlayerState
    {
        public string UserId { get; set; } = "";

        public string SessionToken { get; set; } = "";

        public string ConnectionId { get; set; } = "";

        public string RealtimeSessionId { get; set; } = "";

        public long RealtimeSessionGeneration { get; set; }

        public int SeatIndex { get; set; } = -1;

        public bool IsReady { get; set; }

        public bool IsConnected { get; set; }

        public DateTime JoinedAtUtc { get; set; }

        public DateTime LastSeenAtUtc { get; set; }

        public DateTime LeftAtUtc { get; set; }

        public string LeaveReason { get; set; } = "";

        public int Rank { get; set; }
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
