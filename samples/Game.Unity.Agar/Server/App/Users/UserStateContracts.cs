
using System;
using MemoryPack;

namespace Server.App.Users
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

}
