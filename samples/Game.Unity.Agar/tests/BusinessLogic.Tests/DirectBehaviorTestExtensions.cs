using Server.App.State.Contracts.Leaderboard;
using Server.App.State.Contracts.Matchmaking;
using Server.App.State.Contracts.Rooms;
using Server.App.State.Contracts.Sessions;
using Server.App.State.Contracts.Users;
using Server.App.State.Leaderboard;
using Server.App.State.Matchmaking;
using Server.App.State.Rooms;
using Server.App.State.Users;
using Server.Hotfix.State.Leaderboard;
using Server.Hotfix.State.Matchmaking;
using Server.Hotfix.State.Rooms;
using Server.Hotfix.State.Users;

namespace Agar.Unity.Tests;

// Test-only direct invocation adapters. Production hotfix code uses generated
// ActorAccess method selectors; these helpers keep state-focused
// unit tests inside an actor turn without recreating runtime dispatch.
internal static class DirectBehaviorTestExtensions
{
    public static ValueTask<UserLoginResult> LoginAndAttachAsync(this UserActor actor, UserLoginAndAttachRequest request, CancellationToken ct = default) =>
        new UserBehavior().LoginAndAttachAsync(actor, request, ct);

    public static ValueTask<UserProfileSnapshot> GetProfileAsync(this UserActor actor, UserProfileRequest request, CancellationToken ct = default) =>
        new UserBehavior().GetProfileAsync(actor, request, ct);

    public static ValueTask AddVictoryPointsAsync(this UserActor actor, UserVictoryPointsRequest request, CancellationToken ct = default) =>
        new UserBehavior().AddVictoryPointsAsync(actor, request, ct);

    public static ValueTask<PlayerSessionSnapshot> GetSnapshotAsync(this UserActor actor, PlayerSessionSnapshotRequest request, CancellationToken ct = default) =>
        new UserBehavior().GetSnapshotAsync(actor, request, ct);

    public static ValueTask<PlayerSessionSnapshot> AssignRoomAsync(this UserActor actor, PlayerRoomAssignment request, CancellationToken ct = default) =>
        new UserBehavior().AssignRoomAsync(actor, request, ct);

    public static ValueTask<PlayerSessionSnapshot> AttachRealtimeAsync(this UserActor actor, PlayerRealtimeAttachRequest request, CancellationToken ct = default) =>
        new UserBehavior().AttachRealtimeAsync(actor, request, ct);

    public static ValueTask<PlayerSessionSnapshot> ClearRealtimeAsync(this UserActor actor, PlayerRealtimeClearRequest request, CancellationToken ct = default) =>
        new UserBehavior().ClearRealtimeAsync(actor, request, ct);

    public static ValueTask<RoomSettlementResult> CreateAsync(this RoomActor actor, RoomCreateRequest request, CancellationToken ct = default) =>
        new RoomBehavior().CreateAsync(actor, request, ct);

    public static ValueTask<RoomSettlementResult> SetReadyAsync(this RoomActor actor, RoomPlayerReadyRequest request, CancellationToken ct = default) =>
        new RoomBehavior().SetReadyAsync(actor, request, ct);

    public static ValueTask<RoomSettlementResult> ClearRealtimeAsync(this RoomActor actor, RoomRealtimeClearRequest request, CancellationToken ct = default) =>
        new RoomBehavior().ClearRealtimeAsync(actor, request, ct);

    public static ValueTask<RoomSnapshot> GetSnapshotAsync(this RoomActor actor, RoomSnapshotRequest request, CancellationToken ct = default) =>
        new RoomBehavior().GetSnapshotAsync(actor, request, ct);

    public static ValueTask SubmitInputAsync(this RoomActor actor, RoomInputSubmitRequest request, CancellationToken ct = default) =>
        new RoomBehavior().SubmitInputAsync(actor, request, ct);

    public static ValueTask RunTickAsync(this RoomActor actor, RoomTickRequest request, CancellationToken ct = default) =>
        new RoomBehavior().RunTickAsync(actor, request, ct);

    public static ValueTask<LeaderboardSnapshot> GetLeaderboardAsync(this LeaderboardActor actor, LeaderboardQueryRequest request, CancellationToken ct = default) =>
        new LeaderboardBehavior().GetLeaderboardAsync(actor, request, ct);

    public static ValueTask RecordVictoryPointsAsync(this LeaderboardActor actor, LeaderboardVictoryPointsRequest request, CancellationToken ct = default) =>
        new LeaderboardBehavior().RecordVictoryPointsAsync(actor, request, ct);

    public static ValueTask<MatchmakingEnqueueResult> EnqueueAsync(this MatchmakingActor actor, MatchmakingEnqueueRequest request, CancellationToken ct = default) =>
        new MatchmakingBehavior().EnqueueAsync(actor, request, ct);

    public static ValueTask<MatchmakingStatusSnapshot> GetStatusAsync(this MatchmakingActor actor, MatchmakingStatusRequest request, CancellationToken ct = default) =>
        new MatchmakingBehavior().GetStatusAsync(actor, request, ct);

    public static ValueTask RunTickAsync(this MatchmakingActor actor, MatchmakingTickRequest request, CancellationToken ct = default) =>
        new MatchmakingBehavior().RunTickAsync(actor, request, ct);

    public static ValueTask StartTimerAsync(this MatchmakingActor actor, MatchmakingTimerStartRequest request, CancellationToken ct = default) =>
        new MatchmakingBehavior().StartTimerAsync(actor, request, ct);
}
