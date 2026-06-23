using Agar.Sample.State.Contracts.Sessions;
using Agar.Sample.State.Users;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Server.Hotfix.Services;
using Server.Hotfix.State.Sessions;

namespace Server.Hotfix.Services;

[HotfixLifecycle(typeof(IGameSessionLifecycle))]
public sealed class AgarSessionLifecycle
{
    public static async ValueTask SessionDisconnectedAsync(HotfixLifecycleCall<GameSessionDisconnectedRequest> call)
    {
        var services = AgarLifecycleDependencies.From(call);
        var localActors = services.LocalActors;
        var connection = services.PlayerSessionRegistry.GetConnection(call.Request.ConnectionId);
        if (connection is null)
        {
            return;
        }

        if (connection.Kind == PlayerConnectionKind.Realtime)
        {
            await services.PlayerSessionRegistry
                .DetachRealtimeAsync(connection.PlayerId, connection.ConnectionId)
                .ConfigureAwait(false);
            return;
        }

        try
        {
            await localActors
                .AskAsync<UserActor, PlayerSessionSnapshot>(
                    UserId(connection.PlayerId),
                    (actor, _) => actor.MarkDisconnectedAsync(new PlayerSessionDisconnectRequest
                    {
                        UserId = connection.PlayerId,
                        ConnectionId = connection.ConnectionId,
                        DisconnectedAtUtc = DateTime.UtcNow,
                        Reason = "Control disconnect"
                    }))
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            services.Logger.LogError(
                ex,
                "Failed to mark player {PlayerId} disconnected for control connection {ConnectionId}.",
                connection.PlayerId,
                connection.ConnectionId);
        }

        await services.PlayerSessionRegistry
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

        var registration = services.PlayerSessionRegistry.Get(playerId);
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
            await services.PlayerSessionRegistry
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

    private static ActorId UserId(string userId) => ActorId.From($"session:{userId}");
}

internal sealed record AgarLifecycleDependencies(
    IActorRuntime LocalActors,
    PlayerSessionRegistry PlayerSessionRegistry,
    ILogger<AgarSessionLifecycle> Logger)
{
    public static AgarLifecycleDependencies From<TRequest>(HotfixLifecycleCall<TRequest> call)
    {
        var loggerFactory = call.Services.GetRequiredService<ILoggerFactory>();
        return new AgarLifecycleDependencies(
            call.Actors,
            call.Services.GetRequiredService<PlayerSessionRegistry>(),
            loggerFactory.CreateLogger<AgarSessionLifecycle>());
    }
}
