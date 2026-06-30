namespace Lakona.Game.Server.Observability.Diagnostics;

public interface ILakonaDiagnosticsSnapshotProvider
{
    string Name { get; }

    ValueTask<object> CaptureAsync(CancellationToken cancellationToken);
}
