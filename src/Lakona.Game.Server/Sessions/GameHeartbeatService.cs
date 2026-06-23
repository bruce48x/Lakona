using Lakona.Game.Abstractions.Sessions;

namespace Lakona.Game.Server.Sessions;

public sealed class GameHeartbeatService : IGameHeartbeatService
{
    private readonly IGameSessionRegistry _sessions;

    public GameHeartbeatService(IGameSessionRegistry sessions)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
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

        return result.Status switch
        {
            GameSessionHeartbeatStatus.ConnectionOnly or GameSessionHeartbeatStatus.ActiveSession => new GameHeartbeatReply
            {
                Status = GameHeartbeatStatus.Ok
            },
            GameSessionHeartbeatStatus.Terminated => new GameHeartbeatReply
            {
                Status = GameHeartbeatStatus.Terminated,
                Message = result.Termination?.Message
            },
            _ => new GameHeartbeatReply
            {
                Status = GameHeartbeatStatus.StateLost,
                Message = "Game session state was lost."
            }
        };
    }
}
