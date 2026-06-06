using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Actors;

public sealed class CompositeClusterMessageHandler : IClusterMessageHandler
{
    private readonly IClusterMessageHandler[] _handlers;

    public CompositeClusterMessageHandler(params IClusterMessageHandler[] handlers)
    {
        _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
    }

    public async ValueTask<ClusterSendStatus> HandleAsync(
        ClusterMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        foreach (var handler in _handlers)
        {
            var status = await handler.HandleAsync(message, cancellationToken).ConfigureAwait(false);

            if (status != ClusterSendStatus.RouteNotFound)
            {
                return status;
            }
        }

        return ClusterSendStatus.RouteNotFound;
    }
}
