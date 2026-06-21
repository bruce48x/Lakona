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
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Microsoft.Extensions.Logging;
using Server.App.Hotfix;
using Server.Hotfix.State.Leaderboard;
using Server.Hotfix.State.Matchmaking;
using Server.Hotfix.State.Rooms;
using Server.Hotfix.State.Sessions;
using Server.Hotfix.State.Users;

namespace Server.Hotfix.Services;

[HotfixService(typeof(IAgarRuntimeService))]
public sealed class AgarRuntimeService
{
    public async ValueTask TickMatchmakingAsync(HotfixServiceCall<AgarMatchmakingTickRequest> call)
    {
        var services = AgarServiceDependencies.From(call);
        var assignments = await services.Actors
            .AskAsync<MatchmakingActor, Dictionary<string, RoomAssignment>>(
                DefaultQueueId,
                (actor, _) => actor.TickAsync(new MatchmakingTickRequest
                {
                    ObservedAtUtc = call.Request.ObservedAtUtc
                }))
            .ConfigureAwait(false);

        foreach (var assignment in assignments.Values.DistinctBy(static assignment => assignment.RoomId))
        {
            await PlayerService.PublishMatchedAsync(services, assignment).ConfigureAwait(false);
        }
    }

    public async ValueTask CommitRoomSettlementAsync(HotfixServiceCall<AgarRoomSettlementRequest> call)
    {
        var req = call.Request;
        var services = AgarServiceDependencies.From(call);
        var logger = services.CreateLogger<AgarRuntimeService>();
        var settlement = req.Settlement;

        await services.Actors
            .AskAsync<RoomActor, RoomSettlementResult>(
                RoomId(req.RoomId),
                (actor, _) => actor.CompleteAsync(new RoomMatchCompletion
                {
                    RoomId = req.RoomId,
                    SettlementId = req.SettlementId,
                    FinishedAtUtc = req.FinishedAtUtc == default ? DateTime.UtcNow : req.FinishedAtUtc,
                    WinnerUserId = settlement.WinnerPlayerId,
                    Reason = settlement.Reason,
                    Results = settlement.Entries.Select(entry => new RoomSettlementEntry
                    {
                        UserId = entry.PlayerId,
                        Rank = entry.Rank,
                        Mass = entry.Mass,
                        IsWinner = entry.IsWinner
                    }).ToList()
                }))
            .ConfigureAwait(false);

        foreach (var registration in services.SessionDirectory.GetByRoom(req.RoomId))
        {
            services.SessionDirectory.ClearRoom(registration.PlayerId, req.RoomId);
            await services.Actors
                .AskAsync<PlayerSessionActor, PlayerSessionSnapshot>(
                    SessionId(registration.PlayerId),
                    (actor, _) => actor.ClearRoomAsync(new PlayerRoomClearRequest
                    {
                        UserId = registration.PlayerId,
                        RoomId = req.RoomId,
                        ClearedAtUtc = DateTime.UtcNow,
                        Reason = "Match completed."
                    }))
                .ConfigureAwait(false);
        }

        var winnerEntry = settlement.Entries.FirstOrDefault(static entry => entry.IsWinner);
        if (winnerEntry is not null && !winnerEntry.IsBot)
        {
            await services.Actors
                .TellAsync<UserActor>(
                    UserId(winnerEntry.PlayerId),
                    (actor, _) => actor.AddWinAsync())
                .ConfigureAwait(false);
        }

        foreach (var entry in settlement.Entries.Where(static entry => !entry.IsBot && entry.VictoryPoints > 0))
        {
            await services.Actors
                .TellAsync<UserActor>(
                    UserId(entry.PlayerId),
                    (actor, _) => actor.AddVictoryPointsAsync(entry.VictoryPoints))
                .ConfigureAwait(false);
            var profile = await services.Actors
                .AskAsync<UserActor, UserProfileSnapshot>(
                    UserId(entry.PlayerId),
                    (actor, _) => actor.GetProfileAsync())
                .ConfigureAwait(false);
            await services.Actors
                .TellAsync<LeaderboardActor>(
                    LeaderboardId,
                    (actor, _) => actor.RecordVictoryPointsAsync(entry.PlayerId, profile.VictoryPoints, profile.WinCount))
                .ConfigureAwait(false);
            logger.LogInformation(
                "Awarded {VictoryPoints} victory points to {PlayerId} for rank {Rank} in room {RoomId}.",
                entry.VictoryPoints,
                entry.PlayerId,
                entry.Rank,
                req.RoomId);
        }
    }

    private static readonly ActorId DefaultQueueId = ActorId.From("default");
    private static readonly ActorId LeaderboardId = ActorId.From("current");

    private static ActorId RoomId(string roomId) => ActorId.From(roomId);

    private static ActorId SessionId(string userId) => ActorId.From($"session:{userId}");

    private static ActorId UserId(string userId) => ActorId.From(userId);
}
