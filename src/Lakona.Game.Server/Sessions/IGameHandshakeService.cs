using Lakona.Game.Abstractions.Sessions;

namespace Lakona.Game.Server.Sessions;

public interface IGameHandshakeService
{
    ValueTask<GameServerHello> HandshakeAsync(
        GameClientHello hello,
        string endpointTransport,
        string endpointSerializer,
        bool reliablePush,
        CancellationToken cancellationToken = default);
}
