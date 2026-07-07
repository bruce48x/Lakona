namespace Server.App.State.Contracts.Rooms;

public sealed class RoomSnapshotRequest
{
}

public sealed class RoomTickRequest
{
    public DateTime ObservedAtUtc { get; set; }

    public float DeltaSeconds { get; set; } = 1f / 20f;
}
