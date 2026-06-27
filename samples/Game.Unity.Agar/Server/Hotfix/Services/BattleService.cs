using Agar.Sample.State.Contracts;
using Agar.Sample.State.Contracts.Rooms;
using Agar.Sample.State.Contracts.Sessions;
using Agar.Sample.State.Contracts.Users;
using Agar.Sample.State.Rooms;
using Agar.Sample.State.Users;
using Lakona.Game.Abstractions;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Server.Hotfix.Services;
using Server.Hotfix.State.Rooms;
using Server.Hotfix.State.Sessions;
using Shared.Interfaces;

namespace Server.Hotfix.Services;

[HotfixService(typeof(IBattleService))]
internal sealed class BattleService
{
    private readonly LocalActorNodeIdentity _localNode;
    private readonly RoomActors _rooms;
    private readonly UserActors _users;

    public BattleService(
        UserActors users,
        RoomActors rooms,
        LocalActorNodeIdentity localNode)
    {
        _users = users;
        _rooms = rooms;
        _localNode = localNode;
    }

    public async ValueTask<RealtimeAttachReply> AttachRealtimeAsync(HotfixServiceCall<RealtimeAttachRequest, IBattleCallback> call)
    {
        var req = call.Request;
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

        var sessionSnapshot = await _users
            .Get(new UserId(req.PlayerId))
            .GetSnapshotAsync(new PlayerSessionSnapshotRequest())
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

        if (!IsLocalRuntimeOwner(sessionSnapshot.RuntimeGateway))
        {
            return new RealtimeAttachReply
            {
                Code = 3,
                Message = "Realtime session must attach to the runtime owner gateway."
            };
        }

        var realtimeSession = await call.GameServer
            .StartSessionAsync(req.PlayerId, call.ConnectionId, call.Callback)
            .ConfigureAwait(false);
        try
        {
            sessionSnapshot = await _users
                .Get(new UserId(req.PlayerId))
                .AttachRealtimeAsync(new PlayerRealtimeAttachRequest
                    {
                        UserId = req.PlayerId,
                        SessionToken = req.Token,
                        RoomId = req.RoomId,
                        MatchId = req.MatchId,
                        RealtimeSessionId = realtimeSession.SessionId,
                        RealtimeSessionGeneration = realtimeSession.Generation,
                        AttachedAtUtc = DateTime.UtcNow
                    })
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            await call.GameServer
                .TerminateSessionAsync(
                    realtimeSession,
                    SessionTerminationReason.Unauthorized,
                    "Realtime session attach rejected.")
                .ConfigureAwait(false);
            return new RealtimeAttachReply
            {
                Code = 2,
                Message = "Realtime session attach rejected."
            };
        }

        await _rooms
            .Get(new RoomId(req.RoomId))
            .SetReadyAsync(new RoomPlayerReadyRequest
            {
                UserId = req.PlayerId,
                RoomId = req.RoomId,
                IsReady = true,
                RealtimeSessionId = realtimeSession.SessionId,
                RealtimeSessionGeneration = realtimeSession.Generation,
                UpdatedAtUtc = DateTime.UtcNow
            }).ConfigureAwait(false);

        return new RealtimeAttachReply
        {
            Code = 0,
            Message = "Realtime session attached.",
            PlayerId = req.PlayerId,
            RoomId = req.RoomId,
            MatchId = req.MatchId,
            SessionId = realtimeSession.SessionId,
            SessionGeneration = realtimeSession.Generation
        };
    }

    public async ValueTask SubmitInputAsync(HotfixServiceCall<InputMessage, IBattleCallback> call)
    {
        var req = call.Request;
        var playerId = call.CurrentSession?.OwnerKey;
        // Direct current-node escape hatches should be named `var nodeLocalActors = call.Actors;`;
        // use typed selectors when actor placement may be remote.
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(req.PlayerId) &&
            !string.Equals(req.PlayerId, playerId, StringComparison.Ordinal))
        {
            return;
        }

        var sessionSnapshot = await _users
            .Get(new UserId(playerId))
            .GetSnapshotAsync(new PlayerSessionSnapshotRequest())
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(sessionSnapshot.CurrentRoomId) ||
            !IsLocalRuntimeOwner(sessionSnapshot.RuntimeGateway))
        {
            return;
        }

        await _rooms
            .Get(new RoomId(sessionSnapshot.CurrentRoomId))
            .SubmitInputAsync(new RoomInputSubmitRequest
            {
                RoomId = sessionSnapshot.CurrentRoomId,
                UserId = playerId,
                Input = req,
                SubmittedAtUtc = DateTime.UtcNow
            })
            .ConfigureAwait(false);
    }

    private bool IsLocalRuntimeOwner(GatewayEndpointDescriptor? gateway)
    {
        return gateway is not null &&
            !string.IsNullOrWhiteSpace(gateway.InstanceId) &&
            string.Equals(gateway.InstanceId, _localNode.NodeId.Value, StringComparison.Ordinal);
    }
}
