using Server.App.State.Contracts;
using Server.App.State.Contracts.Rooms;
using Server.App.State.Contracts.Sessions;
using Server.App.State.Contracts.Users;
using Server.App.State.Rooms;
using Server.App.State.Users;
using Lakona.Game.Abstractions;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Sessions;
using Server.Hotfix.Services;
using Server.Hotfix.State.Rooms;
using Server.Hotfix.State.Users;
using Shared.Interfaces;

namespace Server.Hotfix.Services;

[HotfixService(typeof(IBattleService))]
internal sealed class BattleService
{
    private const string RoomIdSessionItemKey = "roomId";
    private const string MatchIdSessionItemKey = "matchId";
    private const string RealtimeSessionIdSessionItemKey = "realtimeSessionId";
    private const string RealtimeSessionGenerationSessionItemKey = "realtimeSessionGeneration";

    private readonly LocalActorNodeIdentity _localNode;
    private readonly ActorAccess _actors;

    public BattleService(
        ActorAccess actors,
        LocalActorNodeIdentity localNode)
    {
        _actors = actors;
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

        var sessionSnapshot = await _actors
            .Route<UserActor>(new UserId(req.PlayerId))
            .CallAsync(
                UserBehavior.GetSnapshotAsync,
                new PlayerSessionSnapshotRequest(),
                CancellationToken.None)
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
            .StartSessionAsync(req.PlayerId, call.ConnectionId)
            .ConfigureAwait(false);
        try
        {
            await _actors
                .Route<UserActor>(new UserId(req.PlayerId))
                .CallAsync(
                    UserBehavior.AttachRealtimeAsync,
                    new PlayerRealtimeAttachRequest
                    {
                        UserId = req.PlayerId,
                        SessionToken = req.Token,
                        RoomId = req.RoomId,
                        MatchId = req.MatchId,
                        RealtimeSessionId = realtimeSession.SessionId,
                        RealtimeSessionGeneration = realtimeSession.Generation
                    },
                    CancellationToken.None)
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

        var ready = await _actors
            .Local<RoomActor>(new RoomId(req.RoomId))
            .CallAsync(
                RoomBehavior.SetReadyAsync,
                new RoomPlayerReadyRequest
            {
                UserId = req.PlayerId,
                RoomId = req.RoomId,
                IsReady = true,
                RealtimeSessionId = realtimeSession.SessionId,
                RealtimeSessionGeneration = realtimeSession.Generation,
                UpdatedAtUtc = DateTime.UtcNow
            },
                CancellationToken.None)
            .ConfigureAwait(false);
        if (!ready.Succeeded)
        {
            await _actors
                .Route<UserActor>(new UserId(req.PlayerId))
                .CallAsync(
                    UserBehavior.ClearRealtimeAsync,
                    new PlayerRealtimeClearRequest
                {
                    UserId = req.PlayerId,
                    RealtimeSessionId = realtimeSession.SessionId,
                    RealtimeSessionGeneration = realtimeSession.Generation,
                    ClearedAtUtc = DateTime.UtcNow,
                    Reason = ready.Message
                },
                    CancellationToken.None)
                .ConfigureAwait(false);
            await call.GameServer
                .TerminateSessionAsync(
                    realtimeSession,
                    SessionTerminationReason.Policy,
                    ready.Message)
                .ConfigureAwait(false);
            return new RealtimeAttachReply
            {
                Code = 4,
                Message = ready.Message
            };
        }

        await call.GameServer
            .SetSessionItemAsync(realtimeSession, RoomIdSessionItemKey, GameSessionItemValue.FromString(req.RoomId))
            .ConfigureAwait(false);
        await call.GameServer
            .SetSessionItemAsync(realtimeSession, MatchIdSessionItemKey, GameSessionItemValue.FromString(req.MatchId))
            .ConfigureAwait(false);
        await call.GameServer
            .SetSessionItemAsync(realtimeSession, RealtimeSessionIdSessionItemKey, GameSessionItemValue.FromString(realtimeSession.SessionId))
            .ConfigureAwait(false);
        await call.GameServer
            .SetSessionItemAsync(realtimeSession, RealtimeSessionGenerationSessionItemKey, GameSessionItemValue.FromInt64(realtimeSession.Generation))
            .ConfigureAwait(false);

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
        var playerId = call.CurrentSession?.OwnerKey;
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(req.PlayerId) &&
            !string.Equals(req.PlayerId, playerId, StringComparison.Ordinal))
        {
            return;
        }

        var roomId = call.CurrentSessionItems.GetString(RoomIdSessionItemKey);
        var realtimeSessionId = call.CurrentSessionItems.GetString(RealtimeSessionIdSessionItemKey);
        var realtimeSessionGeneration = call.CurrentSessionItems.GetInt64(RealtimeSessionGenerationSessionItemKey);
        if (string.IsNullOrWhiteSpace(roomId) ||
            string.IsNullOrWhiteSpace(realtimeSessionId) ||
            realtimeSessionGeneration is null ||
            realtimeSessionGeneration <= 0)
        {
            return;
        }

        await _actors
            .Local<RoomActor>(new RoomId(roomId))
            .CallAsync(
                RoomBehavior.SubmitInputAsync,
                new RoomInputSubmitRequest
            {
                RoomId = roomId,
                UserId = playerId,
                RealtimeSessionId = realtimeSessionId,
                RealtimeSessionGeneration = realtimeSessionGeneration.Value,
                Input = req,
                SubmittedAtUtc = DateTime.UtcNow
            },
                CancellationToken.None)
            .ConfigureAwait(false);
    }

    private bool IsLocalRuntimeOwner(GatewayEndpointDescriptor? gateway)
    {
        return gateway is not null &&
            !string.IsNullOrWhiteSpace(gateway.InstanceId) &&
            string.Equals(gateway.InstanceId, _localNode.NodeId.Value, StringComparison.Ordinal);
    }
}
