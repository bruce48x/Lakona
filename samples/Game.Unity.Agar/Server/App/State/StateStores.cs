using Agar.Sample.State.Contracts.Leaderboard;
using Agar.Sample.State.Contracts.Matchmaking;
using Agar.Sample.State.Contracts.Rooms;
using Agar.Sample.State.Contracts.Sessions;
using Agar.Sample.State.Contracts.Users;
using Agar.Sample.State.Leaderboard;
using Agar.Sample.State.Matchmaking;
using Agar.Sample.State.Rooms;
using Agar.Sample.State.Sessions;
using Agar.Sample.State.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Dispatch;
using Server.App.Services;

namespace Agar.Sample.State;

public interface IUserStateStore
{
    Task<UserLoginResult> LoginAsync(string userId, string password, bool reconnect);
    Task<UserProfileSnapshot> GetProfileAsync(string userId);
    Task SetOnlineAsync(string userId, bool isOnline);
    Task AddWinAsync(string userId);
    Task AddVictoryPointsAsync(string userId, int points);
    Task ResetVictoryPointsAsync(string userId);
}

public interface IPlayerSessionStateStore
{
    Task<PlayerSessionSnapshot> AttachAsync(PlayerSessionAttachRequest request);
    Task<PlayerSessionSnapshot> ReconnectAsync(PlayerSessionReconnectRequest request);
    Task<PlayerSessionSnapshot> MarkQueuedAsync(PlayerSessionQueueRequest request);
    Task<PlayerSessionSnapshot> ClearQueueAsync(PlayerSessionQueueClearRequest request);
    Task<PlayerSessionSnapshot> AssignRoomAsync(PlayerRoomAssignment request);
    Task<PlayerSessionSnapshot> ClearRoomAsync(PlayerRoomClearRequest request);
    Task<PlayerSessionSnapshot> MarkDisconnectedAsync(PlayerSessionDisconnectRequest request);
    Task<PlayerSessionSnapshot> HeartbeatAsync(PlayerSessionHeartbeatRequest request);
    Task<PlayerSessionSnapshot> GetSnapshotAsync(string userId);
}

public interface IMatchmakingStateStore
{
    Task<MatchmakingEnqueueResult> EnqueueAsync(MatchmakingEnqueueRequest request);
    Task<MatchmakingCancelResult> CancelAsync(MatchmakingCancelRequest request);
    Task TickAsync(MatchmakingTickRequest request);
    Task<MatchmakingStatusSnapshot> GetStatusAsync();
}

public interface IRoomStateStore
{
    Task<RoomSettlementResult> CreateAsync(RoomCreateRequest request);
    Task<RoomSettlementResult> LeaveAsync(RoomPlayerLeaveRequest request);
    Task<RoomSettlementResult> StartAsync(RoomStartRequest request);
    Task<RoomSettlementResult> CompleteAsync(RoomMatchCompletion request);
    Task<RoomSnapshot> GetSnapshotAsync(string roomId);
}

public interface ILeaderboardStateStore
{
    Task<LeaderboardSnapshot> GetLeaderboardAsync(int topN);
    Task RecordVictoryPointsAsync(string playerId, int victoryPoints, int winCount);
}

public static class SampleStateServiceCollectionExtensions
{
    public static IServiceCollection AddAgarSampleState(this IServiceCollection services)
    {
        services.AddLakonaGameServerActors();
        services.TryAddSingleton<BattleRuntimeGatewayResolver>();
        services.TryAddSingleton<IUserStateStore, ActorUserStateStore>();
        services.TryAddSingleton<IPlayerSessionStateStore, ActorPlayerSessionStateStore>();
        services.TryAddSingleton<IMatchmakingStateStore, ActorMatchmakingStateStore>();
        services.TryAddSingleton<IRoomStateStore, ActorRoomStateStore>();
        services.TryAddSingleton<ILeaderboardStateStore, ActorLeaderboardStateStore>();
        return services;
    }
}

internal sealed class ActorUserStateStore(IActorRuntime runtime) : IUserStateStore
{
    public Task<UserLoginResult> LoginAsync(string userId, string password, bool reconnect)
    {
        return runtime.AskAsync<UserActor, UserLoginResult>(
            UserId(userId),
            async (actor, _) => await HotfixDispatch.Invoke<UserActor, ValueTask<UserLoginResult>>(
                "LoginAsync",
                actor,
                [typeof(string), typeof(bool)],
                [password, reconnect]).ConfigureAwait(false)).AsTask();
    }

    public Task<UserProfileSnapshot> GetProfileAsync(string userId)
    {
        return runtime.AskAsync<UserActor, UserProfileSnapshot>(
            UserId(userId),
            async (actor, _) => await HotfixDispatch.Invoke<UserActor, ValueTask<UserProfileSnapshot>>(
                "GetProfileAsync",
                actor,
                [],
                []).ConfigureAwait(false)).AsTask();
    }

    public Task SetOnlineAsync(string userId, bool isOnline)
    {
        return runtime.TellAsync<UserActor>(
            UserId(userId),
            async (actor, _) => await HotfixDispatch.Invoke<UserActor, ValueTask>(
                "SetOnlineAsync",
                actor,
                [typeof(bool)],
                [isOnline]).ConfigureAwait(false)).AsTask();
    }

    public Task AddWinAsync(string userId)
    {
        return runtime.TellAsync<UserActor>(
            UserId(userId),
            async (actor, _) => await HotfixDispatch.Invoke<UserActor, ValueTask>(
                "AddWinAsync",
                actor,
                [],
                []).ConfigureAwait(false)).AsTask();
    }

    public Task AddVictoryPointsAsync(string userId, int points)
    {
        return runtime.TellAsync<UserActor>(
            UserId(userId),
            async (actor, _) => await HotfixDispatch.Invoke<UserActor, ValueTask>(
                "AddVictoryPointsAsync",
                actor,
                [typeof(int)],
                [points]).ConfigureAwait(false)).AsTask();
    }

    public Task ResetVictoryPointsAsync(string userId)
    {
        return runtime.TellAsync<UserActor>(
            UserId(userId),
            async (actor, _) => await HotfixDispatch.Invoke<UserActor, ValueTask>(
                "ResetVictoryPointsAsync",
                actor,
                [],
                []).ConfigureAwait(false)).AsTask();
    }

    private static ActorId UserId(string userId) => ActorId.From(userId);
}

internal sealed class ActorPlayerSessionStateStore(IActorRuntime runtime) : IPlayerSessionStateStore
{
    public Task<PlayerSessionSnapshot> AttachAsync(PlayerSessionAttachRequest request)
    {
        return Ask(request.UserId, "AttachAsync", request);
    }

    public Task<PlayerSessionSnapshot> ReconnectAsync(PlayerSessionReconnectRequest request)
    {
        return Ask(request.UserId, "ReconnectAsync", request);
    }

    public Task<PlayerSessionSnapshot> MarkQueuedAsync(PlayerSessionQueueRequest request)
    {
        return Ask(request.UserId, "MarkQueuedAsync", request);
    }

    public Task<PlayerSessionSnapshot> ClearQueueAsync(PlayerSessionQueueClearRequest request)
    {
        return Ask(request.UserId, "ClearQueueAsync", request);
    }

    public Task<PlayerSessionSnapshot> AssignRoomAsync(PlayerRoomAssignment request)
    {
        return Ask(request.UserId, "AssignRoomAsync", request);
    }

    public Task<PlayerSessionSnapshot> ClearRoomAsync(PlayerRoomClearRequest request)
    {
        return Ask(request.UserId, "ClearRoomAsync", request);
    }

    public Task<PlayerSessionSnapshot> MarkDisconnectedAsync(PlayerSessionDisconnectRequest request)
    {
        return Ask(request.UserId, "MarkDisconnectedAsync", request);
    }

    public Task<PlayerSessionSnapshot> HeartbeatAsync(PlayerSessionHeartbeatRequest request)
    {
        return Ask(request.UserId, "HeartbeatAsync", request);
    }

    public Task<PlayerSessionSnapshot> GetSnapshotAsync(string userId)
    {
        return runtime.AskAsync<PlayerSessionActor, PlayerSessionSnapshot>(
            SessionId(userId),
            async (actor, _) => await HotfixDispatch.Invoke<PlayerSessionActor, ValueTask<PlayerSessionSnapshot>>(
                "GetSnapshotAsync",
                actor,
                [],
                []).ConfigureAwait(false)).AsTask();
    }

    private static ActorId SessionId(string userId) => ActorId.From($"session:{userId}");

    private Task<PlayerSessionSnapshot> Ask<TRequest>(string userId, string methodName, TRequest request)
    {
        return runtime.AskAsync<PlayerSessionActor, PlayerSessionSnapshot>(
            SessionId(userId),
            async (actor, _) => await HotfixDispatch.Invoke<PlayerSessionActor, ValueTask<PlayerSessionSnapshot>>(
                methodName,
                actor,
                [typeof(TRequest)],
                [request]).ConfigureAwait(false)).AsTask();
    }
}

internal sealed class ActorMatchmakingStateStore(IActorRuntime runtime) : IMatchmakingStateStore
{
    private static readonly ActorId DefaultQueueId = ActorId.From("default");

    public Task<MatchmakingEnqueueResult> EnqueueAsync(MatchmakingEnqueueRequest request)
    {
        return runtime.AskAsync<MatchmakingActor, MatchmakingEnqueueResult>(
            DefaultQueueId,
            async (actor, _) => await HotfixDispatch.Invoke<MatchmakingActor, ValueTask<MatchmakingEnqueueResult>>(
                "EnqueueAsync",
                actor,
                [typeof(MatchmakingEnqueueRequest)],
                [request]).ConfigureAwait(false)).AsTask();
    }

    public Task<MatchmakingCancelResult> CancelAsync(MatchmakingCancelRequest request)
    {
        return runtime.AskAsync<MatchmakingActor, MatchmakingCancelResult>(
            DefaultQueueId,
            async (actor, _) => await HotfixDispatch.Invoke<MatchmakingActor, ValueTask<MatchmakingCancelResult>>(
                "CancelAsync",
                actor,
                [typeof(MatchmakingCancelRequest)],
                [request]).ConfigureAwait(false)).AsTask();
    }

    public Task TickAsync(MatchmakingTickRequest request)
    {
        return runtime.TellAsync<MatchmakingActor>(
            DefaultQueueId,
            async (actor, _) => await HotfixDispatch.Invoke<MatchmakingActor, ValueTask>(
                "TickAsync",
                actor,
                [typeof(MatchmakingTickRequest)],
                [request]).ConfigureAwait(false)).AsTask();
    }

    public Task<MatchmakingStatusSnapshot> GetStatusAsync()
    {
        return runtime.AskAsync<MatchmakingActor, MatchmakingStatusSnapshot>(
            DefaultQueueId,
            async (actor, _) => await HotfixDispatch.Invoke<MatchmakingActor, ValueTask<MatchmakingStatusSnapshot>>(
                "GetStatusAsync",
                actor,
                [],
                []).ConfigureAwait(false)).AsTask();
    }
}

internal sealed class ActorRoomStateStore(IActorRuntime runtime) : IRoomStateStore
{
    public Task<RoomSettlementResult> CreateAsync(RoomCreateRequest request)
    {
        return Ask(request.RoomId, "CreateAsync", request);
    }

    public Task<RoomSettlementResult> LeaveAsync(RoomPlayerLeaveRequest request)
    {
        return Ask(request.RoomId, "LeaveAsync", request);
    }

    public Task<RoomSettlementResult> StartAsync(RoomStartRequest request)
    {
        return Ask(request.RoomId, "StartAsync", request);
    }

    public Task<RoomSettlementResult> CompleteAsync(RoomMatchCompletion request)
    {
        return Ask(request.RoomId, "CompleteAsync", request);
    }

    public Task<RoomSnapshot> GetSnapshotAsync(string roomId)
    {
        return runtime.AskAsync<RoomActor, RoomSnapshot>(
            ActorId.From(roomId),
            async (actor, _) => await HotfixDispatch.Invoke<RoomActor, ValueTask<RoomSnapshot>>(
                "GetSnapshotAsync",
                actor,
                [],
                []).ConfigureAwait(false)).AsTask();
    }

    private Task<RoomSettlementResult> Ask<TRequest>(string roomId, string methodName, TRequest request)
    {
        return runtime.AskAsync<RoomActor, RoomSettlementResult>(
            ActorId.From(roomId),
            async (actor, _) => await HotfixDispatch.Invoke<RoomActor, ValueTask<RoomSettlementResult>>(
                methodName,
                actor,
                [typeof(TRequest)],
                [request]).ConfigureAwait(false)).AsTask();
    }
}

internal sealed class ActorLeaderboardStateStore(IActorRuntime runtime) : ILeaderboardStateStore
{
    private static readonly ActorId LeaderboardId = ActorId.From("current");

    public Task<LeaderboardSnapshot> GetLeaderboardAsync(int topN)
    {
        return runtime.AskAsync<LeaderboardActor, LeaderboardSnapshot>(
            LeaderboardId,
            async (actor, _) => await HotfixDispatch.Invoke<LeaderboardActor, ValueTask<LeaderboardSnapshot>>(
                "GetLeaderboardAsync",
                actor,
                [typeof(int)],
                [topN]).ConfigureAwait(false)).AsTask();
    }

    public Task RecordVictoryPointsAsync(string playerId, int victoryPoints, int winCount)
    {
        return runtime.TellAsync<LeaderboardActor>(
            LeaderboardId,
            async (actor, _) => await HotfixDispatch.Invoke<LeaderboardActor, ValueTask>(
                "RecordVictoryPointsAsync",
                actor,
                [typeof(string), typeof(int), typeof(int)],
                [playerId, victoryPoints, winCount]).ConfigureAwait(false)).AsTask();
    }
}
