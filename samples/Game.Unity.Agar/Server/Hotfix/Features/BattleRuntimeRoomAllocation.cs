using Server.App.State.Contracts.Sessions;
using Lakona.Game.Server.Hotfix.Abstractions;
using MemoryPack;

namespace Server.Hotfix.Features;

internal static class BattleRuntimeRoomAllocation
{
    public const string FeatureName = "battle-runtime";

    public const int AllocateRoomCommandId = 101;
}

[MemoryPackable(GenerateType.VersionTolerant)]
[FeatureCommand(BattleRuntimeRoomAllocation.AllocateRoomCommandId)]
public partial class BattleRuntimeRoomAllocationRequest
{
    [MemoryPackOrder(0)]
    public string RoomId { get; set; } = "";

    [MemoryPackOrder(1)]
    public int MaxPlayers { get; set; } = 10;

    [MemoryPackOrder(2)]
    public List<PlayerRoomAssignment> Players { get; set; } = new();
}

[MemoryPackable(GenerateType.VersionTolerant)]
public partial class BattleRuntimeRoomAllocationReply
{
    [MemoryPackOrder(0)]
    public bool Succeeded { get; set; }

    [MemoryPackOrder(1)]
    public string RoomId { get; set; } = "";

    [MemoryPackOrder(2)]
    public string Message { get; set; } = "";
}
