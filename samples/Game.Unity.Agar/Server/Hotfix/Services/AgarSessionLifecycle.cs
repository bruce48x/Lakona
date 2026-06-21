using Agar.Sample.State.Contracts.Sessions;
using Agar.Sample.State.Sessions;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Sessions;
using Server.App.Services;
using Server.Hotfix.State.Sessions;

namespace Server.Hotfix.Services;

[HotfixLifecycle(typeof(IGameSessionLifecycle))]
public sealed class AgarSessionLifecycle
{
    public static async ValueTask SessionDisconnectedAsync(HotfixLifecycleCall<GameSessionDisconnectedRequest> call)
    {
        var services = AgarServiceDependencies.From(call);
        var connection = services.SessionDirectory.GetConnection(call.Request.ConnectionId);
        if (connection is null)
        {
            return;
        }

        if (connection.Kind == PlayerConnectionKind.Realtime)
        {
            await services.SessionDirectory
                .DetachRealtimeAsync(connection.PlayerId, connection.ConnectionId)
                .ConfigureAwait(false);
            return;
        }

        await services.Actors
            .AskAsync<PlayerSessionActor, PlayerSessionSnapshot>(
                SessionId(connection.PlayerId),
                (actor, _) => actor.MarkDisconnectedAsync(new PlayerSessionDisconnectRequest
                {
                    UserId = connection.PlayerId,
                    ConnectionId = connection.ConnectionId,
                    DisconnectedAtUtc = DateTime.UtcNow,
                    Reason = "Control disconnect"
                }))
            .ConfigureAwait(false);

        await services.SessionDirectory
            .DisconnectControlAsync(connection.PlayerId, connection.ConnectionId)
            .ConfigureAwait(false);
    }

    public static async ValueTask SessionExpiredAsync(HotfixLifecycleCall<GameSessionExpiredRequest> call)
    {
        var services = AgarServiceDependencies.From(call);
        var playerId = call.Request.OwnerKey;
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return;
        }

        var registration = services.SessionDirectory.Get(playerId);
        if (registration is null)
        {
            return;
        }

        var expiredSession = new GameSessionKey(
            call.Request.OwnerKey,
            call.Request.SessionId,
            call.Request.Generation);
        if (registration.RealtimeSessionKey == expiredSession)
        {
            await services.SessionDirectory
                .DetachRealtimeAsync(playerId, call.Request.ConnectionId)
                .ConfigureAwait(false);
            return;
        }

        if (registration.ControlSessionKey != expiredSession)
        {
            return;
        }

        await PlayerService
            .ReleasePlayerAsync(services, playerId, "Reconnect grace period expired")
            .ConfigureAwait(false);
    }

    private static ActorId SessionId(string userId) => ActorId.From($"session:{userId}");
}
