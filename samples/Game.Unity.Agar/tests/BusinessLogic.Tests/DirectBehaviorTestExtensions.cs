using Server.App.Leaderboard;
using Server.App.Matchmaking;
using Server.App.Rooms;
using Server.App.Sessions;
using Server.App.Users;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions;
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
        CreateBehavior<UserBehavior>(actor).LoginAndAttachAsync(actor, request);

    public static ValueTask AddVictoryPointsAsync(this UserActor actor, UserVictoryPointsRequest request, CancellationToken ct = default) =>
        CreateBehavior<UserBehavior>(actor).AddVictoryPointsAsync(actor, request);

    public static ValueTask AddWinAsync(this UserActor actor, UserWinRequest request, CancellationToken ct = default) =>
        CreateBehavior<UserBehavior>(actor).AddWinAsync(actor, request);

    public static ValueTask<PlayerSessionSnapshot> GetSnapshotAsync(this UserActor actor, PlayerSessionSnapshotRequest request, CancellationToken ct = default) =>
        CreateBehavior<UserBehavior>(actor).GetSnapshotAsync(actor, request);

    public static async ValueTask<PlayerSessionSnapshot> AssignRoomAsync(this UserActor actor, PlayerRoomAssignment request, CancellationToken ct = default)
    {
        var behavior = CreateBehavior<UserBehavior>(actor);
        await behavior.AssignRoomAsync(actor, request);
        return await behavior.GetSnapshotAsync(actor, new PlayerSessionSnapshotRequest());
    }

    public static async ValueTask<PlayerSessionSnapshot> AttachRealtimeAsync(this UserActor actor, PlayerRealtimeAttachRequest request, CancellationToken ct = default)
    {
        var behavior = CreateBehavior<UserBehavior>(actor);
        await behavior.AttachRealtimeAsync(actor, request);
        return await behavior.GetSnapshotAsync(actor, new PlayerSessionSnapshotRequest());
    }

    public static async ValueTask<PlayerSessionSnapshot> ClearRealtimeAsync(this UserActor actor, PlayerRealtimeClearRequest request, CancellationToken ct = default)
    {
        var behavior = CreateBehavior<UserBehavior>(actor);
        await behavior.ClearRealtimeAsync(actor, request);
        return await behavior.GetSnapshotAsync(actor, new PlayerSessionSnapshotRequest());
    }

    public static async ValueTask<RoomSettlementResult> CreateAsync(this RoomActor actor, RoomCreateRequest request, CancellationToken ct = default)
    {
        using var timerScope = TestHotfixTimerScope.Enter();
        return await CreateBehavior<RoomBehavior>(actor).CreateAsync(actor, request).ConfigureAwait(false);
    }

    public static ValueTask<RoomSettlementResult> SetReadyAsync(this RoomActor actor, RoomPlayerReadyRequest request, CancellationToken ct = default) =>
        CreateBehavior<RoomBehavior>(actor).SetReadyAsync(actor, request);

    public static ValueTask<RoomSettlementResult> ClearRealtimeAsync(this RoomActor actor, RoomRealtimeClearRequest request, CancellationToken ct = default) =>
        CreateBehavior<RoomBehavior>(actor).ClearRealtimeAsync(actor, request);

    public static ValueTask<RoomSnapshot> GetSnapshotAsync(this RoomActor actor, RoomSnapshotRequest request, CancellationToken ct = default) =>
        CreateBehavior<RoomBehavior>(actor).GetSnapshotAsync(actor, request);

    public static ValueTask SubmitInputAsync(this RoomActor actor, RoomInputSubmitRequest request, CancellationToken ct = default) =>
        CreateBehavior<RoomBehavior>(actor).SubmitInputAsync(actor, request);

    public static async ValueTask RunFrameAsync(this RoomActor actor, RoomFrameRequest request)
    {
        using var timerScope = TestHotfixTimerScope.Enter();
        await CreateBehavior<RoomBehavior>(actor).RunFrameAsync(actor, request).ConfigureAwait(false);
    }

    public static ValueTask<LeaderboardSnapshot> GetLeaderboardAsync(this LeaderboardActor actor, LeaderboardQueryRequest request, CancellationToken ct = default) =>
        CreateBehavior<LeaderboardBehavior>(actor).GetLeaderboardAsync(actor, request);

    public static ValueTask RecordVictoryPointsAsync(this LeaderboardActor actor, LeaderboardVictoryPointsRequest request, CancellationToken ct = default) =>
        CreateBehavior<LeaderboardBehavior>(actor).RecordVictoryPointsAsync(actor, request);

    public static ValueTask<MatchmakingEnqueueResult> EnqueueAsync(this MatchmakingActor actor, MatchmakingEnqueueRequest request, CancellationToken ct = default) =>
        CreateBehavior<MatchmakingBehavior>(actor).EnqueueAsync(actor, request);

    public static ValueTask RunTickAsync(this MatchmakingActor actor, MatchmakingTickRequest request) =>
        CreateBehavior<MatchmakingBehavior>(actor).RunTickAsync(actor, request);

    public static ValueTask StartMatchmakingLifecycleAsync(this MatchmakingActor actor, CancellationToken ct = default) =>
        CreateBehavior<MatchmakingBehavior>(actor).StartAsync(
            actor,
            new ActorStartCall(actor.Context.Id, actor.Context.Services, ct));

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
