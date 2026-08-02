using Server.App.Leaderboard;
using Server.App.Matchmaking;
using Server.App.Rooms;
using Server.App.Sessions;
using Server.App.Users;
using Lakona.Game.Server.Actors;
using Microsoft.Extensions.DependencyInjection;
using Server.Hotfix.Leaderboard;
using Server.Hotfix.Matchmaking;
using Server.Hotfix.Rooms;
using Server.Hotfix.Users;

namespace Agar.Unity.Tests;

// Test-only direct invocation adapters. Production hotfix code uses generated
// ActorAccess method selectors; these helpers keep state-focused
// unit tests inside an actor turn without recreating runtime dispatch.
internal static class DirectBehaviorTestExtensions
{
    public static ValueTask<UserLoginResult> LoginAndAttachAsync(this UserActor actor, UserLoginAndAttachRequest request, CancellationToken ct = default) =>
        CreateBehavior<UserBehavior>(actor).LoginAndAttachAsync(actor, request, ct);

    public static ValueTask<UserProfileSnapshot> GetProfileAsync(this UserActor actor, UserProfileRequest request, CancellationToken ct = default) =>
        CreateBehavior<UserBehavior>(actor).GetProfileAsync(actor, request, ct);

    public static ValueTask AddVictoryPointsAsync(this UserActor actor, UserVictoryPointsRequest request, CancellationToken ct = default) =>
        CreateBehavior<UserBehavior>(actor).AddVictoryPointsAsync(actor, request, ct);

    public static ValueTask<PlayerSessionSnapshot> GetSnapshotAsync(this UserActor actor, PlayerSessionSnapshotRequest request, CancellationToken ct = default) =>
        CreateBehavior<UserBehavior>(actor).GetSnapshotAsync(actor, request, ct);

    public static ValueTask<PlayerSessionSnapshot> AssignRoomAsync(this UserActor actor, PlayerRoomAssignment request, CancellationToken ct = default) =>
        CreateBehavior<UserBehavior>(actor).AssignRoomAsync(actor, request, ct);

    public static ValueTask<PlayerSessionSnapshot> AttachRealtimeAsync(this UserActor actor, PlayerRealtimeAttachRequest request, CancellationToken ct = default) =>
        CreateBehavior<UserBehavior>(actor).AttachRealtimeAsync(actor, request, ct);

    public static ValueTask<PlayerSessionSnapshot> ClearRealtimeAsync(this UserActor actor, PlayerRealtimeClearRequest request, CancellationToken ct = default) =>
        CreateBehavior<UserBehavior>(actor).ClearRealtimeAsync(actor, request, ct);

    public static ValueTask<RoomSettlementResult> CreateAsync(this RoomActor actor, RoomCreateRequest request, CancellationToken ct = default) =>
        CreateBehavior<RoomBehavior>(actor).CreateAsync(actor, request, ct);

    public static ValueTask<RoomSettlementResult> SetReadyAsync(this RoomActor actor, RoomPlayerReadyRequest request, CancellationToken ct = default) =>
        CreateBehavior<RoomBehavior>(actor).SetReadyAsync(actor, request, ct);

    public static ValueTask<RoomSettlementResult> ClearRealtimeAsync(this RoomActor actor, RoomRealtimeClearRequest request, CancellationToken ct = default) =>
        CreateBehavior<RoomBehavior>(actor).ClearRealtimeAsync(actor, request, ct);

    public static ValueTask<RoomSnapshot> GetSnapshotAsync(this RoomActor actor, RoomSnapshotRequest request, CancellationToken ct = default) =>
        CreateBehavior<RoomBehavior>(actor).GetSnapshotAsync(actor, request, ct);

    public static ValueTask SubmitInputAsync(this RoomActor actor, RoomInputSubmitRequest request, CancellationToken ct = default) =>
        CreateBehavior<RoomBehavior>(actor).SubmitInputAsync(actor, request, ct);

    public static ValueTask RunFrameAsync(this RoomActor actor, RoomFrameRequest request, CancellationToken ct = default) =>
        CreateBehavior<RoomBehavior>(actor).RunFrameAsync(actor, request, ct);

    public static ValueTask<LeaderboardSnapshot> GetLeaderboardAsync(this LeaderboardActor actor, LeaderboardQueryRequest request, CancellationToken ct = default) =>
        CreateBehavior<LeaderboardBehavior>(actor).GetLeaderboardAsync(actor, request, ct);

    public static ValueTask RecordVictoryPointsAsync(this LeaderboardActor actor, LeaderboardVictoryPointsRequest request, CancellationToken ct = default) =>
        CreateBehavior<LeaderboardBehavior>(actor).RecordVictoryPointsAsync(actor, request, ct);

    public static ValueTask<MatchmakingEnqueueResult> EnqueueAsync(this MatchmakingActor actor, MatchmakingEnqueueRequest request, CancellationToken ct = default) =>
        CreateBehavior<MatchmakingBehavior>(actor).EnqueueAsync(actor, request, ct);

    public static ValueTask<MatchmakingStatusSnapshot> GetStatusAsync(this MatchmakingActor actor, MatchmakingStatusRequest request, CancellationToken ct = default) =>
        CreateBehavior<MatchmakingBehavior>(actor).GetStatusAsync(actor, request, ct);

    public static ValueTask RunTickAsync(this MatchmakingActor actor, MatchmakingTickRequest request, CancellationToken ct = default) =>
        CreateBehavior<MatchmakingBehavior>(actor).RunTickAsync(actor, request, ct);

    public static ValueTask StartTimerAsync(this MatchmakingActor actor, MatchmakingTimerStartRequest request, CancellationToken ct = default) =>
        CreateBehavior<MatchmakingBehavior>(actor).StartTimerAsync(actor, request, ct);

    private static TBehavior CreateBehavior<TBehavior>(Actor actor)
        where TBehavior : class
    {
        return ActivatorUtilities.CreateInstance<TBehavior>(
            new DirectBehaviorActivationServices(actor.Context.Services));
    }

    private sealed class DirectBehaviorActivationServices(IServiceProvider services) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(MatchmakingNotifier) || serviceType == typeof(RoomNotifier))
            {
                return ActivatorUtilities.CreateInstance(services, serviceType);
            }

            return services.GetService(serviceType);
        }
    }
}
