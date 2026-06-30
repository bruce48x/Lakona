using System.Diagnostics;

namespace Lakona.Game.Server.Observability.Diagnostics;

public sealed class ProcessDiagnosticsProvider : ILakonaDiagnosticsSnapshotProvider
{
    public string Name => "process";

    public ValueTask<object> CaptureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var process = Process.GetCurrentProcess();
        var snapshot = new ProcessDiagnosticsSnapshot(
            Environment.ProcessId,
            Math.Max(0, (long)(DateTimeOffset.UtcNow - process.StartTime.ToUniversalTime()).TotalSeconds),
            process.WorkingSet64,
            GC.GetTotalMemory(forceFullCollection: false));

        return new ValueTask<object>(snapshot);
    }

    private sealed record ProcessDiagnosticsSnapshot(
        int ProcessId,
        long UptimeSeconds,
        long WorkingSetBytes,
        long GcHeapBytes);
}
