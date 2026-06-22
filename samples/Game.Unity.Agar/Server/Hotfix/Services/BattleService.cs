using Agar.Sample.State.Contracts.Rooms;
using Agar.Sample.State.Contracts.Sessions;
using Agar.Sample.State.Rooms;
using Agar.Sample.State.Sessions;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Server.App.Services;
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
        var localActors = call.Actors;
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

        var sessionSnapshot = await localActors
            .AskAsync<PlayerSessionActor, PlayerSessionSnapshot>(
                SessionId(req.PlayerId),
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

        if (!services.GatewayNodeIdentity.IsRuntimeOwner(sessionSnapshot.RuntimeGateway))
        {
            return new RealtimeAttachReply
            {
                Code = 3,
                Message = "Realtime session must attach to the runtime owner gateway."
            };
        }

        var attached = await services.SessionDirectory
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

        await localActors.AskAsync<RoomActor, RoomSettlementResult>(
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
        var localActors = call.Actors;
        var playerId = services.SessionDirectory.GetPlayerIdByConnection(call.ConnectionId);
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(req.PlayerId) &&
            !string.Equals(req.PlayerId, playerId, StringComparison.Ordinal))
        {
            return;
        }

        var sessionSnapshot = await localActors
            .AskAsync<PlayerSessionActor, PlayerSessionSnapshot>(
                SessionId(playerId),
                (actor, _) => actor.GetSnapshotAsync())
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(sessionSnapshot.CurrentRoomId) ||
            !services.GatewayNodeIdentity.IsRuntimeOwner(sessionSnapshot.RuntimeGateway))
        {
            return;
        }

        await localActors.TellAsync<RoomActor>(
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

    private static ActorId SessionId(string userId) => ActorId.From($"session:{userId}");
}

internal sealed record AgarBattleServiceDependencies(
    SessionDirectory SessionDirectory,
    GatewayNodeIdentity GatewayNodeIdentity)
{
    public static AgarBattleServiceDependencies From<TRequest>(HotfixServiceCall<TRequest> call)
    {
        return new AgarBattleServiceDependencies(
            call.Services.GetRequiredService<SessionDirectory>(),
            call.Services.GetRequiredService<GatewayNodeIdentity>());
    }
}
