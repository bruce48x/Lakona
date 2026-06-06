#nullable enable

using System.Collections.Generic;
using System.Threading.Tasks;
using MemoryPack;
using Lakona.Rpc.Core;
using UnityEngine;

namespace Shared.Interfaces
{
    [RpcService(1, NotificationContract = typeof(IPlayerCallback))]
    public interface IPlayerService
    {
        [RpcMethod(1)]
        ValueTask<LoginReply> LoginAsync(LoginRequest req);

        [RpcMethod(4)]
        ValueTask StartMatchmakingAsync(MatchmakingRequest req);

        [RpcMethod(5)]
        ValueTask CancelMatchmakingAsync(CancelMatchmakingRequest req);

        [RpcMethod(6)]
        ValueTask<RealtimeAttachReply> AttachRealtimeAsync(RealtimeAttachRequest req);

        [RpcMethod(7)]
        ValueTask<ReliablePushAckReply> AckReliablePushAsync(ReliablePushAckRequest req);

        [RpcMethod(8)]
        ValueTask<LeaderboardReply> GetLeaderboardAsync(LeaderboardRequest req);
        
        [RpcMethod(2)]
        ValueTask SubmitInput(InputMessage req);

        [RpcMethod(3)]
        ValueTask LogoutAsync(LogoutRequest req);
    }

    [RpcNotificationContract(typeof(IPlayerService))]
    public interface IPlayerCallback
    {
        [RpcNotification(1)]
        void OnWorldState(WorldState worldState);

        [RpcNotification(2)]
        void OnPlayerDead(PlayerDead deadEvent);

        [RpcNotification(3)]
        void OnMatchEnd(MatchEnd matchEnd);

        [RpcNotification(4)]
        void OnMatchmakingStatus(MatchmakingStatusUpdate matchmakingStatus) { }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class LoginRequest
    {
        [MemoryPackOrder(0)]
        public string Account { get; set; } = "";
        [MemoryPackOrder(1)]
        public string Password { get; set; } = "";
        [MemoryPackOrder(2)]
        public bool GuestLogin { get; set; }
        [MemoryPackOrder(3)]
        public bool Reconnect { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class LoginReply
    {
        [MemoryPackOrder(0)]
        public int Code { get; set; }
        [MemoryPackOrder(1)]
        public string Token { get; set; } = "";
        [MemoryPackOrder(2)]
        public string PlayerId { get; set; } = "";
        [MemoryPackOrder(3)]
        public int WinCount { get; set; }
        [MemoryPackOrder(4)]
        public string Account { get; set; } = "";
        [MemoryPackOrder(5)]
        public string Password { get; set; } = "";
        [MemoryPackOrder(6)]
        public string Message { get; set; } = "";
        [MemoryPackOrder(7)]
        public int VictoryPoints { get; set; }
        [MemoryPackOrder(8)]
        public string SessionId { get; set; } = "";
        [MemoryPackOrder(9)]
        public long SessionGeneration { get; set; }
    }

    public static class LoginResultCodes
    {
        public const int Ok = 0;
        public const int InvalidRequest = 1;
        public const int Rejected = 2;
        public const int ReconnectStateLost = 3;
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class InputMessage
    {
        [MemoryPackOrder(0)]
        public string PlayerId { get; set; } = "";
        [MemoryPackOrder(1)]
        public float MoveX { get; set; }
        [MemoryPackOrder(2)]
        public float MoveY { get; set; }
        [MemoryPackOrder(3)]
        public int Tick { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class LogoutRequest
    {
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class MatchmakingRequest
    {
        [MemoryPackOrder(0)]
        public string PlayerId { get; set; } = "";
        [MemoryPackOrder(1)]
        public string Token { get; set; } = "";
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class CancelMatchmakingRequest
    {
        [MemoryPackOrder(0)]
        public string PlayerId { get; set; } = "";
        [MemoryPackOrder(1)]
        public string Token { get; set; } = "";
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class ReliablePushAckRequest
    {
        [MemoryPackOrder(0)]
        public string PlayerId { get; set; } = "";
        [MemoryPackOrder(1)]
        public string Token { get; set; } = "";
        [MemoryPackOrder(2)]
        public long Sequence { get; set; }
        [MemoryPackOrder(3)]
        public string SessionId { get; set; } = "";
        [MemoryPackOrder(4)]
        public long SessionGeneration { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class ReliablePushAckReply
    {
        [MemoryPackOrder(0)]
        public int Code { get; set; }
        [MemoryPackOrder(1)]
        public bool RequiresNewSession { get; set; }
        [MemoryPackOrder(2)]
        public string Message { get; set; } = "";
    }

    public static class ReliablePushAckResultCodes
    {
        public const int Ok = 0;
        public const int InvalidRequest = 1;
        public const int SessionStateLost = 2;
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class RealtimeAttachRequest
    {
        [MemoryPackOrder(0)]
        public string PlayerId { get; set; } = "";
        [MemoryPackOrder(1)]
        public string Token { get; set; } = "";
        [MemoryPackOrder(2)]
        public string RoomId { get; set; } = "";
        [MemoryPackOrder(3)]
        public string MatchId { get; set; } = "";
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class RealtimeAttachReply
    {
        [MemoryPackOrder(0)]
        public int Code { get; set; }
        [MemoryPackOrder(1)]
        public string Message { get; set; } = "";
        [MemoryPackOrder(2)]
        public string PlayerId { get; set; } = "";
        [MemoryPackOrder(3)]
        public string RoomId { get; set; } = "";
        [MemoryPackOrder(4)]
        public string MatchId { get; set; } = "";
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class MatchmakingStatusUpdate
    {
        [MemoryPackOrder(0)]
        public MatchmakingState State { get; set; }
        [MemoryPackOrder(1)]
        public string Message { get; set; } = "";
        [MemoryPackOrder(2)]
        public string RoomId { get; set; } = "";
        [MemoryPackOrder(3)]
        public int QueuePosition { get; set; }
        [MemoryPackOrder(4)]
        public int QueueSize { get; set; }
        [MemoryPackOrder(5)]
        public int RoomCapacity { get; set; }
        [MemoryPackOrder(6)]
        public int MatchedPlayerCount { get; set; }
        [MemoryPackOrder(7)]
        public RealtimeConnectionInfo? RealtimeConnection { get; set; }
        [MemoryPackOrder(8)]
        public long ReliableSequence { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class RealtimeConnectionInfo
    {
        [MemoryPackOrder(0)]
        public RealtimeTransportKind Transport { get; set; }
        [MemoryPackOrder(1)]
        public string Host { get; set; } = "";
        [MemoryPackOrder(2)]
        public int Port { get; set; }
        [MemoryPackOrder(3)]
        public string Path { get; set; } = "";
        [MemoryPackOrder(4)]
        public string RoomId { get; set; } = "";
        [MemoryPackOrder(5)]
        public string MatchId { get; set; } = "";
        [MemoryPackOrder(6)]
        public string SessionToken { get; set; } = "";
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class WorldState
    {
        [MemoryPackOrder(0)]
        public int Tick { get; set; }
        [MemoryPackOrder(1)]
        public int RespawnDelaySeconds { get; set; }
        [MemoryPackOrder(2)]
        public List<PlayerState> Players { get; set; } = new();
        [MemoryPackOrder(3)]
        public List<PickupState> Pickups { get; set; } = new();
        [MemoryPackOrder(4)]
        public float ArenaHalfExtentX { get; set; }
        [MemoryPackOrder(5)]
        public float ArenaHalfExtentY { get; set; }
        // Seconds remaining in the current round; 0 when no active round.
        [MemoryPackOrder(6)]
        public int RoundRemainingSeconds { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class PlayerState
    {
        [MemoryPackOrder(0)]
        public string PlayerId { get; set; } = "";
        [MemoryPackOrder(1)]
        public float X { get; set; }
        [MemoryPackOrder(2)]
        public float Y { get; set; }
        [MemoryPackOrder(3)]
        public float Vx { get; set; }
        [MemoryPackOrder(4)]
        public float Vy { get; set; }
        [MemoryPackOrder(5)]
        public PlayerLifeState State { get; set; }
        [MemoryPackOrder(6)]
        public bool Alive { get; set; }
        [MemoryPackOrder(7)]
        public int RespawnRemainingSeconds { get; set; }
        [MemoryPackOrder(8)]
        public float Mass { get; set; }
        [MemoryPackOrder(9)]
        public float Radius { get; set; }
        [MemoryPackOrder(10)]
        public float MoveSpeed { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class LeaderboardRequest
    {
        [MemoryPackOrder(0)]
        public int TopN { get; set; } = 10;
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class LeaderboardReply
    {
        [MemoryPackOrder(0)]
        public int Code { get; set; }
        [MemoryPackOrder(1)]
        public string Message { get; set; } = "";
        [MemoryPackOrder(2)]
        public string PeriodStartUtc { get; set; } = "";
        [MemoryPackOrder(3)]
        public int SecondsUntilReset { get; set; }
        [MemoryPackOrder(4)]
        public List<LeaderboardEntry> Entries { get; set; } = new();
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class LeaderboardEntry
    {
        [MemoryPackOrder(0)]
        public string PlayerId { get; set; } = "";
        [MemoryPackOrder(1)]
        public int VictoryPoints { get; set; }
        [MemoryPackOrder(2)]
        public int WinCount { get; set; }
        [MemoryPackOrder(3)]
        public int Rank { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class PickupState
    {
        [MemoryPackOrder(0)]
        public PickupType Type { get; set; }
        [MemoryPackOrder(1)]
        public float X { get; set; }
        [MemoryPackOrder(2)]
        public float Y { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class PlayerDead
    {
        [MemoryPackOrder(0)]
        public string PlayerId { get; set; } = "";
        [MemoryPackOrder(1)]
        public int Tick { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public partial class MatchEnd
    {
        [MemoryPackOrder(0)]
        public string WinnerPlayerId { get; set; } = "";
        [MemoryPackOrder(1)]
        public int Tick { get; set; }
    }

    public enum PlayerLifeState
    {
        Idle = 0,
        Move = 1,
        Dead = 2
    }

    public enum PickupType
    {
        MassPoint = 0
    }

    public enum MatchmakingState
    {
        Unknown = 0,
        Queued = 1,
        Searching = 2,
        Matched = 3,
        Canceled = 4,
        Failed = 5
    }

    public enum RealtimeTransportKind
    {
        Unknown = 0,
        WebSocket = 1,
        Kcp = 2
    }
}
