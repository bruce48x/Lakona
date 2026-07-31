namespace Lakona.Game.Server.Hosting;

public interface IClusterNodeDescriptorRefresher
{
    ValueTask RefreshAsync(CancellationToken cancellationToken = default);

    ValueTask MarkUnavailableAsync(CancellationToken cancellationToken = default);
}
