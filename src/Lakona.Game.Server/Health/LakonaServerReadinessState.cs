using Lakona.Game.Server.Guardrails;

namespace Lakona.Game.Server.Health;

internal sealed class LakonaServerReadinessState
{
    internal const string PendingCode = "LAKONA150";
    internal const string FailedCode = "LAKONA151";
    internal const string StoppingCode = "LAKONA152";

    private Snapshot snapshot = Snapshot.Starting;

    public IReadOnlyList<LakonaGameDiagnostic> Diagnostics
    {
        get
        {
            var current = Volatile.Read(ref snapshot);
            return current.Status switch
            {
                Status.Ready => [],
                Status.Failed =>
                [
                    new LakonaGameDiagnostic(
                        FailedCode,
                        LakonaGameDiagnosticSeverity.Error,
                        $"Application module '{current.ModuleName}' failed during startup: {current.Message}",
                        "Restore the failed application dependency and restart the server.")
                ],
                Status.Stopping =>
                [
                    new LakonaGameDiagnostic(
                        StoppingCode,
                        LakonaGameDiagnosticSeverity.Error,
                        "The Lakona server is shutting down.",
                        "Route application traffic to another ready node.")
                ],
                _ =>
                [
                    new LakonaGameDiagnostic(
                        PendingCode,
                        LakonaGameDiagnosticSeverity.Error,
                        "Lakona application modules and framework startup have not completed.",
                        "Wait for server startup to complete.")
                ]
            };
        }
    }

    public void MarkReady()
    {
        _ = Interlocked.CompareExchange(
            ref snapshot,
            Snapshot.Ready,
            Snapshot.Starting);
    }

    public void MarkFailed(Type moduleType, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(moduleType);
        ArgumentNullException.ThrowIfNull(exception);
        Volatile.Write(
            ref snapshot,
            new Snapshot(
                Status.Failed,
                moduleType.FullName ?? moduleType.Name,
                string.IsNullOrWhiteSpace(exception.Message)
                    ? exception.GetType().Name
                    : exception.Message));
    }

    public void MarkStopping()
    {
        while (true)
        {
            var current = Volatile.Read(ref snapshot);
            if (current.Status is Status.Failed or Status.Stopping)
            {
                return;
            }

            if (ReferenceEquals(
                Interlocked.CompareExchange(
                    ref snapshot,
                    Snapshot.Stopping,
                    current),
                current))
            {
                return;
            }
        }
    }

    private sealed record Snapshot(
        Status Status,
        string? ModuleName,
        string? Message)
    {
        public static Snapshot Starting { get; } =
            new(Status.Starting, null, null);

        public static Snapshot Ready { get; } =
            new(Status.Ready, null, null);

        public static Snapshot Stopping { get; } =
            new(Status.Stopping, null, null);
    }

    private enum Status
    {
        Starting,
        Ready,
        Failed,
        Stopping
    }
}
