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
using Lakona.Game.Server.Actors;

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
        services.AddSingleton<IUserStateStore, ActorUserStateStore>();
        services.AddSingleton<IPlayerSessionStateStore, ActorPlayerSessionStateStore>();
        services.AddSingleton<IMatchmakingStateStore, ActorMatchmakingStateStore>();
        services.AddSingleton<IRoomStateStore, ActorRoomStateStore>();
        services.AddSingleton<ILeaderboardStateStore, ActorLeaderboardStateStore>();
        return services;
    }
}

internal sealed class ActorUserStateStore(IActorRuntime runtime) : IUserStateStore
{
    public Task<UserLoginResult> LoginAsync(string userId, string password, bool reconnect)
    {
        return runtime.AskAsync<UserActor, UserLoginResult>(
            UserId(userId),
            (actor, _) => new ValueTask<UserLoginResult>(actor.LoginAsync(password, reconnect))).AsTask();
    }

    public Task<UserProfileSnapshot> GetProfileAsync(string userId)
    {
        return runtime.AskAsync<UserActor, UserProfileSnapshot>(
            UserId(userId),
            static (actor, _) => new ValueTask<UserProfileSnapshot>(actor.GetProfileAsync())).AsTask();
    }

    public Task SetOnlineAsync(string userId, bool isOnline)
    {
        return runtime.TellAsync<UserActor>(
            UserId(userId),
            (actor, _) => new ValueTask(actor.SetOnlineAsync(isOnline))).AsTask();
    }

    public Task AddWinAsync(string userId)
    {
        return runtime.TellAsync<UserActor>(
            UserId(userId),
            static (actor, _) => new ValueTask(actor.AddWinAsync())).AsTask();
    }

    public Task AddVictoryPointsAsync(string userId, int points)
    {
        return runtime.TellAsync<UserActor>(
            UserId(userId),
            (actor, _) => new ValueTask(actor.AddVictoryPointsAsync(points))).AsTask();
    }

    public Task ResetVictoryPointsAsync(string userId)
    {
        return runtime.TellAsync<UserActor>(
            UserId(userId),
            static (actor, _) => new ValueTask(actor.ResetVictoryPointsAsync())).AsTask();
    }

    private static ActorId UserId(string userId) => ActorId.From(userId);
}

internal sealed class ActorPlayerSessionStateStore(IActorRuntime runtime) : IPlayerSessionStateStore
{
    public Task<PlayerSessionSnapshot> AttachAsync(PlayerSessionAttachRequest request)
    {
        return Ask(request.UserId, actor => actor.AttachAsync(request));
    }

    public Task<PlayerSessionSnapshot> ReconnectAsync(PlayerSessionReconnectRequest request)
    {
        return Ask(request.UserId, actor => actor.ReconnectAsync(request));
    }

    public Task<PlayerSessionSnapshot> MarkQueuedAsync(PlayerSessionQueueRequest request)
    {
        return Ask(request.UserId, actor => actor.MarkQueuedAsync(request));
    }

    public Task<PlayerSessionSnapshot> ClearQueueAsync(PlayerSessionQueueClearRequest request)
    {
        return Ask(request.UserId, actor => actor.ClearQueueAsync(request));
    }

    public Task<PlayerSessionSnapshot> AssignRoomAsync(PlayerRoomAssignment request)
    {
        return Ask(request.UserId, actor => actor.AssignRoomAsync(request));
    }

    public Task<PlayerSessionSnapshot> ClearRoomAsync(PlayerRoomClearRequest request)
    {
        return Ask(request.UserId, actor => actor.ClearRoomAsync(request));
    }

    public Task<PlayerSessionSnapshot> MarkDisconnectedAsync(PlayerSessionDisconnectRequest request)
    {
        return Ask(request.UserId, actor => actor.MarkDisconnectedAsync(request));
    }

    public Task<PlayerSessionSnapshot> HeartbeatAsync(PlayerSessionHeartbeatRequest request)
    {
        return Ask(request.UserId, actor => actor.HeartbeatAsync(request));
    }

    public Task<PlayerSessionSnapshot> GetSnapshotAsync(string userId)
    {
        return Ask(userId, static actor => actor.GetSnapshotAsync());
    }

    private static ActorId SessionId(string userId) => ActorId.From($"session:{userId}");

    private Task<PlayerSessionSnapshot> Ask(string userId, Func<PlayerSessionActor, Task<PlayerSessionSnapshot>> call)
    {
        return runtime.AskAsync<PlayerSessionActor, PlayerSessionSnapshot>(
            SessionId(userId),
            (actor, _) => new ValueTask<PlayerSessionSnapshot>(call(actor))).AsTask();
    }
}

internal sealed class ActorMatchmakingStateStore(IActorRuntime runtime) : IMatchmakingStateStore
{
    private static readonly ActorId DefaultQueueId = ActorId.From("default");

    public Task<MatchmakingEnqueueResult> EnqueueAsync(MatchmakingEnqueueRequest request)
    {
        return runtime.AskAsync<MatchmakingActor, MatchmakingEnqueueResult>(
            DefaultQueueId,
            (actor, _) => new ValueTask<MatchmakingEnqueueResult>(actor.EnqueueAsync(request))).AsTask();
    }

    public Task<MatchmakingCancelResult> CancelAsync(MatchmakingCancelRequest request)
    {
        return runtime.AskAsync<MatchmakingActor, MatchmakingCancelResult>(
            DefaultQueueId,
            (actor, _) => new ValueTask<MatchmakingCancelResult>(actor.CancelAsync(request))).AsTask();
    }

    public Task TickAsync(MatchmakingTickRequest request)
    {
        return runtime.TellAsync<MatchmakingActor>(
            DefaultQueueId,
            (actor, _) => new ValueTask(actor.TickAsync(request))).AsTask();
    }

    public Task<MatchmakingStatusSnapshot> GetStatusAsync()
    {
        return runtime.AskAsync<MatchmakingActor, MatchmakingStatusSnapshot>(
            DefaultQueueId,
            static (actor, _) => new ValueTask<MatchmakingStatusSnapshot>(actor.GetStatusAsync())).AsTask();
    }
}

internal sealed class ActorRoomStateStore(IActorRuntime runtime) : IRoomStateStore
{
    public Task<RoomSettlementResult> CreateAsync(RoomCreateRequest request)
    {
        return Ask(request.RoomId, actor => actor.CreateAsync(request));
    }

    public Task<RoomSettlementResult> LeaveAsync(RoomPlayerLeaveRequest request)
    {
        return Ask(request.RoomId, actor => actor.LeaveAsync(request));
    }

    public Task<RoomSettlementResult> StartAsync(RoomStartRequest request)
    {
        return Ask(request.RoomId, actor => actor.StartAsync(request));
    }

    public Task<RoomSettlementResult> CompleteAsync(RoomMatchCompletion request)
    {
        return Ask(request.RoomId, actor => actor.CompleteAsync(request));
    }

    public Task<RoomSnapshot> GetSnapshotAsync(string roomId)
    {
        return runtime.AskAsync<RoomActor, RoomSnapshot>(
            ActorId.From(roomId),
            static (actor, _) => new ValueTask<RoomSnapshot>(actor.GetSnapshotAsync())).AsTask();
    }

    private Task<RoomSettlementResult> Ask(string roomId, Func<RoomActor, Task<RoomSettlementResult>> call)
    {
        return runtime.AskAsync<RoomActor, RoomSettlementResult>(
            ActorId.From(roomId),
            (actor, _) => new ValueTask<RoomSettlementResult>(call(actor))).AsTask();
    }
}

internal sealed class ActorLeaderboardStateStore(IActorRuntime runtime) : ILeaderboardStateStore
{
    private static readonly ActorId LeaderboardId = ActorId.From("current");

    public Task<LeaderboardSnapshot> GetLeaderboardAsync(int topN)
    {
        return runtime.AskAsync<LeaderboardActor, LeaderboardSnapshot>(
            LeaderboardId,
            (actor, _) => new ValueTask<LeaderboardSnapshot>(actor.GetLeaderboardAsync(topN))).AsTask();
    }

    public Task RecordVictoryPointsAsync(string playerId, int victoryPoints, int winCount)
    {
        return runtime.TellAsync<LeaderboardActor>(
            LeaderboardId,
            (actor, _) => new ValueTask(actor.RecordVictoryPointsAsync(playerId, victoryPoints, winCount))).AsTask();
    }
}
