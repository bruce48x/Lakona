using Lakona.Game.Abstractions;

namespace Lakona.Game.Server.Sessions;

public interface IGameSessionEndpointCloser
{
    ValueTask CloseEndpointAsync(
        SessionEndpointKey endpoint,
        string connectionId,
        SessionTerminationNotice notice,
        CancellationToken cancellationToken = default);
}
