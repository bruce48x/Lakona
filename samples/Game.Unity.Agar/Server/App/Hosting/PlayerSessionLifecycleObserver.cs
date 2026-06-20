using Lakona.Rpc.Server;
using Microsoft.Extensions.Logging;
using Server.App.Hotfix;
using Server.App.Services;

namespace Server.App.Hosting;

internal sealed class PlayerSessionLifecycleObserver : IRpcSessionLifecycleObserver
{
    private readonly SessionDirectory _sessionDirectory;
    private readonly AgarHotfixRuntimeEvents _hotfixEvents;
    private readonly ILogger<PlayerSessionLifecycleObserver> _logger;

    public PlayerSessionLifecycleObserver(
        SessionDirectory sessionDirectory,
        AgarHotfixRuntimeEvents hotfixEvents,
        ILogger<PlayerSessionLifecycleObserver> logger)
    {
        _sessionDirectory = sessionDirectory;
        _hotfixEvents = hotfixEvents;
        _logger = logger;
    }

    public ValueTask OnSessionStartedAsync(
        RpcSessionLifecycleContext context,
        CancellationToken cancellationToken = default)
    {
        return default;
    }

    public async ValueTask OnSessionDisconnectedAsync(
        RpcSessionLifecycleContext context,
        Exception? error,
        CancellationToken cancellationToken = default)
    {
        var connection = _sessionDirectory.GetConnection(context.ConnectionId);
        if (connection is null)
        {
            return;
        }

        if (connection.Kind == PlayerConnectionKind.Realtime)
        {
            await _sessionDirectory
                .DetachRealtimeAsync(connection.PlayerId, connection.ConnectionId, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        await MarkControlDisconnectedAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask MarkControlDisconnectedAsync(
        PlayerConnectionRegistration connection,
        CancellationToken cancellationToken)
    {
        var disconnectedAtUtc = DateTime.UtcNow;
        try
        {
            await _hotfixEvents
                .MarkControlDisconnectedAsync(
                    connection.PlayerId,
                    connection.ConnectionId,
                    disconnectedAtUtc,
                    "Control disconnect",
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to mark control disconnect for player {PlayerId}.",
                connection.PlayerId);
        }

        await _sessionDirectory
            .MarkControlDisconnectedAsync(
                connection.PlayerId,
                connection.ConnectionId,
                disconnectedAtUtc,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
