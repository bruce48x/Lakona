using Agar.Sample.State.Contracts.Rooms;
using Agar.Sample.State.Contracts.Sessions;
using Agar.Sample.State.Rooms;
using Agar.Sample.State.Users;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Server.Hotfix.Services;
using Server.Hotfix.State.Rooms;
using Server.Hotfix.State.Sessions;
using Shared.Interfaces;

namespace Server.Hotfix.Services;

[HotfixService(typeof(IBattleService))]
public sealed class BattleService
{
    public async ValueTask<RealtimeAttachReply> AttachRealtimeAsync(HotfixServiceCall<RealtimeAttachRequest, IBattleCallback> call)
    {
        var req = call.Request;
        var services = AgarBattleServiceDependencies.From(call);
        var nodeLocalActors = call.Actors;
        if (string.IsNullOrWhiteSpace(req.PlayerId) ||
            string.IsNullOrWhiteSpace(req.Token) ||
            string.IsNullOrWhiteSpace(req.RoomId) ||
            string.IsNullOrWhiteSpace(req.MatchId))
        {
            return new RealtimeAttachReply
            {
                Code = 1,
                Message = "Realtime attach request is incomplete."
            };
        }

        var sessionSnapshot = await nodeLocalActors
            .AskAsync<UserActor, PlayerSessionSnapshot>(
                UserId(req.PlayerId),
                (actor, _) => actor.GetSnapshotAsync())
            .ConfigureAwait(false);
        if (!string.Equals(sessionSnapshot.SessionToken, req.Token, StringComparison.Ordinal) ||
            !string.Equals(sessionSnapshot.CurrentRoomId, req.RoomId, StringComparison.Ordinal) ||
            !string.Equals(sessionSnapshot.CurrentMatchId, req.MatchId, StringComparison.Ordinal))
        {
            return new RealtimeAttachReply
            {
                Code = 2,
                Message = "Realtime session attach rejected."
            };
        }

        if (!services.RuntimeNodeIdentity.IsRuntimeOwner(sessionSnapshot.RuntimeGateway))
        {
            return new RealtimeAttachReply
            {
                Code = 3,
                Message = "Realtime session must attach to the runtime owner gateway."
            };
        }

        var attached = await services.PlayerSessionRegistry
            .AttachRealtimeAsync(req.PlayerId, req.Token, req.RoomId, req.MatchId, call.ConnectionId, call.Callback)
            .ConfigureAwait(false);
        if (!attached)
        {
            return new RealtimeAttachReply
            {
                Code = 2,
                Message = "Realtime session attach rejected."
            };
        }

        await nodeLocalActors.AskAsync<RoomActor, RoomSettlementResult>(
            RoomId(req.RoomId),
            (actor, _) => actor.SetReadyAsync(new RoomPlayerReadyRequest
            {
                UserId = req.PlayerId,
                RoomId = req.RoomId,
                IsReady = true,
                UpdatedAtUtc = DateTime.UtcNow
            })).ConfigureAwait(false);

        return new RealtimeAttachReply
        {
            Code = 0,
            Message = "Realtime session attached.",
            PlayerId = req.PlayerId,
            RoomId = req.RoomId,
            MatchId = req.MatchId
        };
    }

    public async ValueTask SubmitInputAsync(HotfixServiceCall<InputMessage, IBattleCallback> call)
    {
        var req = call.Request;
        var services = AgarBattleServiceDependencies.From(call);
        var nodeLocalActors = call.Actors;
        var playerId = services.PlayerSessionRegistry.GetPlayerIdByConnection(call.ConnectionId);
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(req.PlayerId) &&
            !string.Equals(req.PlayerId, playerId, StringComparison.Ordinal))
        {
            return;
        }

        var sessionSnapshot = await nodeLocalActors
            .AskAsync<UserActor, PlayerSessionSnapshot>(
                UserId(playerId),
                (actor, _) => actor.GetSnapshotAsync())
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(sessionSnapshot.CurrentRoomId) ||
            !services.RuntimeNodeIdentity.IsRuntimeOwner(sessionSnapshot.RuntimeGateway))
        {
            return;
        }

        await nodeLocalActors.TellAsync<RoomActor>(
            RoomId(sessionSnapshot.CurrentRoomId),
            (actor, _) => actor.SubmitInputAsync(new RoomInputSubmitRequest
            {
                RoomId = sessionSnapshot.CurrentRoomId,
                UserId = playerId,
                Input = req,
                SubmittedAtUtc = DateTime.UtcNow
            }))
            .ConfigureAwait(false);
    }

    private static ActorId RoomId(string roomId) => ActorId.From(roomId);

    private static ActorId UserId(string userId) => ActorId.From($"session:{userId}");
}

internal sealed record AgarBattleServiceDependencies(
    PlayerSessionRegistry PlayerSessionRegistry,
    RuntimeNodeIdentity RuntimeNodeIdentity)
{
    public static AgarBattleServiceDependencies From<TRequest>(HotfixServiceCall<TRequest> call)
    {
        return new AgarBattleServiceDependencies(
            call.Services.GetRequiredService<PlayerSessionRegistry>(),
            call.Services.GetRequiredService<RuntimeNodeIdentity>());
    }
}
