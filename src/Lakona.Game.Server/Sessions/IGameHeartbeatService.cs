using Lakona.Game.Abstractions.Sessions;

namespace Lakona.Game.Server.Sessions;

public interface IGameHeartbeatService
{
    ValueTask<GameHeartbeatReply> HeartbeatAsync(
        string connectionId,
        GameHeartbeatRequest request,
        CancellationToken cancellationToken = default);
}
