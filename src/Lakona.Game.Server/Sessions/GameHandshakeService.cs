using Lakona.Game.Abstractions.Sessions;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.ReliablePush;

namespace Lakona.Game.Server.Sessions;

public sealed class GameHandshakeService : IGameHandshakeService
{
    private readonly LakonaGameRuntimeOptions _runtime;
    private readonly ReliablePushOptions _reliablePush;

    public GameHandshakeService(
        LakonaGameRuntimeOptions runtime,
        ReliablePushOptions reliablePush)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _reliablePush = reliablePush ?? throw new ArgumentNullException(nameof(reliablePush));
    }

    public ValueTask<GameServerHello> HandshakeAsync(
        GameClientHello hello,
        string endpointTransport,
        string endpointSerializer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hello);
        cancellationToken.ThrowIfCancellationRequested();

        if (hello.ProtocolVersion != 1)
        {
            throw new GameHandshakeRejectedException(
                "Client does not support Lakona game handshake protocol version 1.");
        }

        var reliable = _reliablePush.Enabled;
        return new ValueTask<GameServerHello>(new GameServerHello
        {
            SelectedProtocolVersion = 1,
            ServerNodeId = _runtime.Node.Id,
            EndpointTransport = endpointTransport,
            EndpointSerializer = endpointSerializer,
            ServerTimeUtc = DateTimeOffset.UtcNow,
            ReliablePush = new ReliablePushHandshakeSettings
            {
                Enabled = reliable,
                DeliveryMode = reliable ? "reliable" : "immediate",
                AckRequired = reliable,
                ReplaySupported = reliable,
                MaxPending = _reliablePush.MaxPendingPerOwner
            },
            ServerCapabilities =
            {
                "business-rpc-after-handshake"
            }
        });
    }
}

internal sealed class GameHandshakeRejectedException : InvalidOperationException
{
    public GameHandshakeRejectedException(string message)
        : base(message)
    {
    }
}
