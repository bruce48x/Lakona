using MemoryPack;

namespace Server.App.Rooms;

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class RoomSnapshotRequest
{
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class RoomFrameRequest
{
    [MemoryPackOrder(0)]
    public DateTime ObservedAtUtc { get; set; }
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class RoomFrameSyncSnapshotRequest
{
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class RoomFrameSyncSnapshot
{
    [MemoryPackOrder(0)]
    public Shared.Interfaces.FrameSyncStart? Start { get; set; }

    [MemoryPackOrder(1)]
    public List<Shared.Interfaces.FrameSyncFrame> Frames { get; set; } = new();
}
