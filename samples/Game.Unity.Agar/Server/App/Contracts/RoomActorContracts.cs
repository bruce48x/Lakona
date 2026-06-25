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

    ValueTask<RoomSettlementResult> StartAsync(RoomStartRequest request, CancellationToken cancellationToken = default);

    ValueTask<RoomSettlementResult> CompleteAsync(RoomMatchCompletion request, CancellationToken cancellationToken = default);

    ValueTask<RoomSnapshot> GetSnapshotAsync(RoomSnapshotRequest request, CancellationToken cancellationToken = default);

    ValueTask SubmitInputAsync(RoomInputSubmitRequest request, CancellationToken cancellationToken = default);
}

public sealed class RoomSnapshotRequest
{
}
