namespace Lakona.Game.Server.Hosting;

public interface IClusterNodeRegistrationRefresher
{
    ValueTask RefreshAsync(CancellationToken cancellationToken = default);
}
