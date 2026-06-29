using Agar.Sample.State.Contracts;
using Agar.Sample.State.Contracts.Rooms;
using Agar.Sample.State.Contracts.Sessions;
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
    public string MatchId { get; set; } = "";

    [MemoryPackOrder(2)]
    public string CreatedByUserId { get; set; } = "";

    [MemoryPackOrder(3)]
    public DateTime CreatedAtUtc { get; set; }

    [MemoryPackOrder(4)]
    public int MaxPlayers { get; set; } = 10;

    [MemoryPackOrder(5)]
    public List<PlayerRoomAssignment> Players { get; set; } = new();

    [MemoryPackOrder(6)]
    public GatewayEndpointDescriptor RuntimeGateway { get; set; } = new();
}

[MemoryPackable(GenerateType.VersionTolerant)]
public partial class BattleRuntimeRoomAllocationReply
{
    [MemoryPackOrder(0)]
    public bool Succeeded { get; set; }

    [MemoryPackOrder(1)]
    public string RoomId { get; set; } = "";

    [MemoryPackOrder(2)]
    public string MatchId { get; set; } = "";

    [MemoryPackOrder(3)]
    public string Message { get; set; } = "";

    [MemoryPackOrder(4)]
    public GatewayEndpointDescriptor RuntimeGateway { get; set; } = new();
}
