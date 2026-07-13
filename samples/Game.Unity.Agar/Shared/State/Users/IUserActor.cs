
using System;
using MemoryPack;

namespace Server.App.State.Contracts.Users
{
    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class UserLoginResult
    {
        [MemoryPackOrder(0)]
        public string UserId { get; set; } = "";

        [MemoryPackOrder(1)]
        public string SessionToken { get; set; } = "";

        [MemoryPackOrder(2)]
        public int LoginCount { get; set; }

        [MemoryPackOrder(3)]
        public DateTime LastLoginAtUtc { get; set; }

        [MemoryPackOrder(4)]
        public int WinCount { get; set; }

        [MemoryPackOrder(5)]
        public int VictoryPoints { get; set; }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial class UserProfileSnapshot
    {
        [MemoryPackOrder(0)]
        public string UserId { get; set; } = "";

        [MemoryPackOrder(1)]
        public int LoginCount { get; set; }

        [MemoryPackOrder(2)]
        public DateTime CreatedAtUtc { get; set; }

        [MemoryPackOrder(3)]
        public DateTime LastLoginAtUtc { get; set; }

        [MemoryPackOrder(4)]
        public bool IsOnline { get; set; }

        [MemoryPackOrder(5)]
        public int WinCount { get; set; }

        [MemoryPackOrder(6)]
        public int VictoryPoints { get; set; }

        [MemoryPackOrder(7)]
        public string SessionToken { get; set; } = "";

        [MemoryPackOrder(8)]
        public string ControlSessionId { get; set; } = "";

        [MemoryPackOrder(9)]
        public long ControlGeneration { get; set; }

        [MemoryPackOrder(10)]
        public string ControlConnectionId { get; set; } = "";

        [MemoryPackOrder(12)]
        public string RealtimeSessionId { get; set; } = "";

        [MemoryPackOrder(13)]
        public long RealtimeGeneration { get; set; }

        [MemoryPackOrder(14)]
        public string RealtimeConnectionId { get; set; } = "";

        [MemoryPackOrder(15)]
        public string RealtimeGatewayNodeId { get; set; } = "";

        [MemoryPackOrder(16)]
        public string CurrentRoomId { get; set; } = "";

        [MemoryPackOrder(17)]
        public string CurrentMatchId { get; set; } = "";

        [MemoryPackOrder(18)]
        public int SeatIndex { get; set; } = -1;

        [MemoryPackOrder(19)]
        public string MatchmakingTicketId { get; set; } = "";
    }

}
