using MemoryPack;

namespace Server.App.State.Contracts.Rooms;

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class RoomSnapshotRequest
{
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class RoomTickRequest
{
    [MemoryPackOrder(0)]
    public DateTime ObservedAtUtc { get; set; }

    [MemoryPackOrder(1)]
    public float DeltaSeconds { get; set; } = 1f / 20f;
}
