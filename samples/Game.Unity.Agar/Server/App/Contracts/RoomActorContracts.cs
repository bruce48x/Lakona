using Agar.Sample.State.Contracts.Sessions;
using Agar.Sample.State.Rooms;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Agar.Sample.State.Contracts.Rooms;

[HotfixActorContract(typeof(RoomActor))]
public interface IRoomActorContract
{
    ValueTask<RoomSettlementResult> CreateAsync(RoomCreateRequest request, CancellationToken cancellationToken = default);

    ValueTask<RoomSettlementResult> JoinAsync(PlayerRoomAssignment request, CancellationToken cancellationToken = default);

    ValueTask<RoomSettlementResult> LeaveAsync(RoomPlayerLeaveRequest request, CancellationToken cancellationToken = default);

    ValueTask<RoomSettlementResult> SetReadyAsync(RoomPlayerReadyRequest request, CancellationToken cancellationToken = default);

    ValueTask<RoomSettlementResult> ClearRealtimeAsync(RoomRealtimeClearRequest request, CancellationToken cancellationToken = default);

    ValueTask<RoomSettlementResult> StartAsync(RoomStartRequest request, CancellationToken cancellationToken = default);

    ValueTask<RoomSettlementResult> CompleteAsync(RoomMatchCompletion request, CancellationToken cancellationToken = default);

    ValueTask<RoomSnapshot> GetSnapshotAsync(RoomSnapshotRequest request, CancellationToken cancellationToken = default);

    ValueTask SubmitInputAsync(RoomInputSubmitRequest request, CancellationToken cancellationToken = default);

    ValueTask RunTickAsync(RoomTickRequest request, CancellationToken cancellationToken = default);
}

public sealed class RoomSnapshotRequest
{
}

public sealed class RoomTickRequest
{
    public DateTime ObservedAtUtc { get; set; }

    public float DeltaSeconds { get; set; } = 1f / 20f;
}
