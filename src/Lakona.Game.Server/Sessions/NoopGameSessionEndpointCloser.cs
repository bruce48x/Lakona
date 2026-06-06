using Lakona.Game.Abstractions;

namespace Lakona.Game.Server.Sessions;

internal sealed class NoopGameSessionEndpointCloser : IGameSessionEndpointCloser
{
    public ValueTask CloseEndpointAsync(
        SessionEndpointKey endpoint,
        string connectionId,
        SessionTerminationNotice notice,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }
}
