namespace Lakona.Game.Server.Hosting;

public interface IClusterNodeDescriptorRefresher
{
    ValueTask RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently fences the local process from distributed admission.
    /// Once initiated, this terminal transition runs to its bounded drain result
    /// and cannot be canceled by the caller.
    /// </summary>
    ValueTask MarkUnavailableAsync();
}
