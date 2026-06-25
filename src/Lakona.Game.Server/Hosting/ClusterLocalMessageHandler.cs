using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Hosting;

internal sealed class ClusterLocalMessageHandler : IClusterMessageHandler
{
    private IClusterMessageHandler? _handler;

    public void SetHandler(IClusterMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        Volatile.Write(ref _handler, handler);
    }

    public ValueTask<ClusterSendStatus> HandleAsync(
        ClusterMessage message,
        CancellationToken cancellationToken = default)
    {
        var handler = Volatile.Read(ref _handler);
        if (handler is null)
        {
            return new ValueTask<ClusterSendStatus>(ClusterSendStatus.RouteNotFound);
        }

        return handler.HandleAsync(message, cancellationToken);
    }
}
