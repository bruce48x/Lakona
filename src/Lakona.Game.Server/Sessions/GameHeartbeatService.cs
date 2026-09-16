using Lakona.Game.Abstractions;
using Lakona.Game.Abstractions.Sessions;
using Lakona.Game.Server.ReliablePush;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lakona.Game.Server.Sessions;

public sealed class GameHeartbeatService : IGameHeartbeatService
{
    private readonly IGameSessionRegistry _sessions;
    private readonly IReliablePushRuntime? _reliablePush;
    private readonly IReadOnlyList<IGameSessionLifecycleHandler> _handlers = [];
    private readonly ILogger<GameHeartbeatService> _logger = NullLogger<GameHeartbeatService>.Instance;

    public GameHeartbeatService(IGameSessionRegistry sessions)
        : this(sessions, (IReliablePushRuntime?)null)
    {
    }

    public GameHeartbeatService(IGameSessionRegistry sessions, IServiceProvider services)
        : this(
            sessions,
            (services ?? throw new ArgumentNullException(nameof(services)))
                .GetService<IReliablePushRuntime>())
    {
        _handlers = services.GetServices<IGameSessionLifecycleHandler>().ToArray();
        _logger = services.GetService<ILogger<GameHeartbeatService>>() ?? _logger;
    }

    internal GameHeartbeatService(
        IGameSessionRegistry sessions,
        IReliablePushRuntime? reliablePush)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _reliablePush = reliablePush;
    }

    public async ValueTask<GameHeartbeatReply> HeartbeatAsync(
        string connectionId,
        GameHeartbeatRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.ProtocolVersion != 1)
        {
            return new GameHeartbeatReply
            {
                Status = GameHeartbeatStatus.StateLost,
                Message = "Unsupported heartbeat protocol version."
            };
        }

        var result = await _sessions.RecordHeartbeatAsync(
            connectionId,
            DateTimeOffset.UtcNow,
            cancellationToken).ConfigureAwait(false);

        switch (result.Status)
        {
            case GameSessionHeartbeatStatus.ConnectionOnly:
                return new GameHeartbeatReply
                {
                    Status = GameHeartbeatStatus.Ok
                };
            case GameSessionHeartbeatStatus.ActiveSession:
            {
                var activeSession = result.Session!.Value;
                if (!string.IsNullOrWhiteSpace(request.SessionId) &&
                    !string.Equals(request.SessionId, activeSession.SessionId, StringComparison.Ordinal))
                {
                    return new GameHeartbeatReply
                    {
                        Status = GameHeartbeatStatus.StateLost,
                        Message = "Client game session does not match the active server session."
                    };
                }

                if (!string.IsNullOrWhiteSpace(request.SessionId))
                {
                    await ReplayPendingAsync(activeSession, cancellationToken).ConfigureAwait(false);
                    var resumed = await _sessions.TakeResumedSessionAsync(activeSession, connectionId, cancellationToken).ConfigureAwait(false);
                    if (resumed is not null)
                    {
                        var context = new GameSessionBindingContext(resumed.Session, resumed.ConnectionId);
                        foreach (var handler in _handlers)
                        {
                            try
                            {
                                await handler.OnSessionResumedAsync(context, CancellationToken.None).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Game session-resumed lifecycle handler failed for {ConnectionId}.", connectionId);
                            }
                        }
                    }
                }

                return new GameHeartbeatReply
                {
                    Status = GameHeartbeatStatus.Ok
                };
            }
            case GameSessionHeartbeatStatus.Terminated:
                return new GameHeartbeatReply
            {
                Status = GameHeartbeatStatus.Terminated,
                Message = result.Termination?.Message
            };
            default:
                return new GameHeartbeatReply
                {
                    Status = GameHeartbeatStatus.StateLost,
                    Message = "Game session state was lost."
                };
        }
    }

    private async ValueTask ReplayPendingAsync(
        GameSessionKey session,
        CancellationToken cancellationToken)
    {
        if (_reliablePush is not null)
        {
            await _reliablePush.ReplayPendingAsync(session, cancellationToken).ConfigureAwait(false);
        }
    }
}
