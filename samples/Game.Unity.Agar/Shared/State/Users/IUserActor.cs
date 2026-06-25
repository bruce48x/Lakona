
using System;

namespace Agar.Sample.State.Contracts.Users
{
    public sealed class UserLoginResult
    {
        public string UserId { get; set; } = "";

        public string SessionToken { get; set; } = "";

        public int LoginCount { get; set; }

        public DateTime LastLoginAtUtc { get; set; }

        public int WinCount { get; set; }
        public int VictoryPoints { get; set; }
    }

    public sealed class UserProfileSnapshot
    {
        public string UserId { get; set; } = "";

        public int LoginCount { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime LastLoginAtUtc { get; set; }

        public bool IsOnline { get; set; }

        public int WinCount { get; set; }

        public int VictoryPoints { get; set; }

        public string SessionToken { get; set; } = "";

        public string ControlSessionId { get; set; } = "";

        public long ControlGeneration { get; set; }

        public string ControlConnectionId { get; set; } = "";

        public string ControlGatewayNodeId { get; set; } = "";

        public string RealtimeSessionId { get; set; } = "";

        public long RealtimeGeneration { get; set; }

        public string RealtimeConnectionId { get; set; } = "";

        public string RealtimeGatewayNodeId { get; set; } = "";

        public string CurrentRoomId { get; set; } = "";

        public string CurrentMatchId { get; set; } = "";

        public int SeatIndex { get; set; } = -1;

        public string MatchmakingTicketId { get; set; } = "";
    }

}
