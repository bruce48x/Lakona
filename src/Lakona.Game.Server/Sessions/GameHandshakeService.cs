using Lakona.Game.Abstractions.Sessions;
using Lakona.Game.Server.Configuration;

namespace Lakona.Game.Server.Sessions;

public sealed class GameHandshakeService : IGameHandshakeService
{
    private readonly LakonaGameRuntimeOptions _runtime;
    private readonly LakonaGameHostingOptions _hosting;

    public GameHandshakeService(
        LakonaGameRuntimeOptions runtime,
        LakonaGameHostingOptions hosting)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _hosting = hosting ?? throw new ArgumentNullException(nameof(hosting));
    }

    public ValueTask<GameServerHello> HandshakeAsync(
        GameClientHello hello,
        string endpointTransport,
        string endpointSerializer,
        bool reliablePush,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hello);
        cancellationToken.ThrowIfCancellationRequested();
        _ = endpointTransport;
        _ = endpointSerializer;

        if (hello.ProtocolVersion != 1)
        {
            throw new GameHandshakeRejectedException(
                "Client does not support Lakona game handshake protocol version 1.");
        }

        return new ValueTask<GameServerHello>(new GameServerHello
        {
            SelectedProtocolVersion = 1,
            ReliablePush = new ReliablePushHandshakeSettings
            {
                Enabled = reliablePush,
                AckRequired = reliablePush,
            },
            SessionResume = new GameSessionResumeHandshakeSettings
            {
                Window = _hosting.Sessions.ResumeWindow,
            },
            Heartbeat = new GameHeartbeatHandshakeSettings
            {
                Interval = _runtime.Heartbeat.Interval,
                Timeout = _runtime.Heartbeat.Timeout
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
