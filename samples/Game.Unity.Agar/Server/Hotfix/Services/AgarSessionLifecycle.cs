using Agar.Sample.State.Contracts.Sessions;
using Agar.Sample.State.Users;
using Agar.Sample.State.Contracts;
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
        var connection = services.PlayerSessionRegistry.GetConnection(call.Request.ConnectionId);
        if (connection is null)
        {
            return;
        }

        if (connection.Kind == PlayerConnectionKind.Realtime)
        {
            services.PlayerSessionRegistry.DetachRealtime(connection.PlayerId, connection.ConnectionId);
            return;
        }

        try
        {
            var users = call.Services.GetRequiredService<UserActors>();
            await users
                .Get(new UserId(connection.PlayerId))
                .MarkDisconnectedAsync(new PlayerSessionDisconnectRequest
                    {
                        UserId = connection.PlayerId,
                        ConnectionId = connection.ConnectionId,
                        DisconnectedAtUtc = DateTime.UtcNow,
                        Reason = "Control disconnect"
                    })
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

        services.PlayerSessionRegistry.DisconnectControl(connection.PlayerId, connection.ConnectionId);
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
            services.PlayerSessionRegistry.DetachRealtime(playerId, call.Request.ConnectionId);
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
}

internal sealed record AgarLifecycleDependencies(
    PlayerSessionRegistry PlayerSessionRegistry,
    ILogger<AgarSessionLifecycle> Logger)
{
    public static AgarLifecycleDependencies From<TRequest>(HotfixLifecycleCall<TRequest> call)
    {
        var loggerFactory = call.Services.GetRequiredService<ILoggerFactory>();
        return new AgarLifecycleDependencies(
            call.Services.GetRequiredService<PlayerSessionRegistry>(),
            loggerFactory.CreateLogger<AgarSessionLifecycle>());
    }
}
