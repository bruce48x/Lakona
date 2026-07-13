using MemoryPack;
using Server.App.State.Contracts;

namespace Server.App.State.Contracts.Users;

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class UserLoginAndAttachRequest
{
    [MemoryPackOrder(0)]
    public string Password { get; set; } = "";

    [MemoryPackOrder(1)]
    public string ConnectionId { get; set; } = "";

    [MemoryPackOrder(2)]
    public string ControlSessionId { get; set; } = "";

    [MemoryPackOrder(3)]
    public long ControlSessionGeneration { get; set; }

    [MemoryPackOrder(4)]
    public DateTime AttachedAtUtc { get; set; }

    [MemoryPackOrder(5)]
    public GatewayEndpointDescriptor ControlGateway { get; set; } = new();
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class UserProfileRequest
{
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class UserOnlineStatusRequest
{
    [MemoryPackOrder(0)]
    public bool IsOnline { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class UserWinRequest
{
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class UserVictoryPointsRequest
{
    [MemoryPackOrder(0)]
    public int Points { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class UserVictoryPointsResetRequest
{
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class PlayerSessionSnapshotRequest
{
}
