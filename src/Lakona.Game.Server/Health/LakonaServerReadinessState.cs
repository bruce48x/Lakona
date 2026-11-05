using System.Runtime.ExceptionServices;
using Lakona.Game.Server.Guardrails;

namespace Lakona.Game.Server.Health;

internal sealed class LakonaServerReadinessState
{
    internal const string PendingCode = "LAKONA10150";
    internal const string FailedCode = "LAKONA10151";
    internal const string StoppingCode = "LAKONA10152";
    internal const string ListenerFailedCode = "LAKONA10154";

    private Snapshot snapshot = Snapshot.Starting;

    internal ExceptionDispatchInfo? ListenerFailure => Volatile.Read(ref snapshot).ListenerFailure;

    public IReadOnlyList<LakonaGameDiagnostic> Diagnostics
    {
        get
        {
            var current = Volatile.Read(ref snapshot);
            return current.Status switch
            {
                Status.Ready => [],
                Status.Failed => [current.Diagnostic!],
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
                        "Lakona application components and framework startup have not completed.",
                        "Wait for server startup to complete.")
                ]
            };
        }
    }

    public bool MarkReady() =>
        ReferenceEquals(Interlocked.CompareExchange(ref snapshot, Snapshot.Ready, Snapshot.Starting), Snapshot.Starting);

    public void MarkFailed(Type componentType, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(componentType);
        ArgumentNullException.ThrowIfNull(exception);
        TrySetTerminal(new Snapshot(Status.Failed, new LakonaGameDiagnostic(
            FailedCode,
            LakonaGameDiagnosticSeverity.Error,
            $"Application component '{componentType.FullName ?? componentType.Name}' failed during startup: {Describe(exception)}",
            "Restore the failed application dependency and restart the server.")));
    }

    internal bool TryMarkListenerFailed(string transport, string address, Exception exception) =>
        TrySetTerminal(new Snapshot(Status.Failed, new LakonaGameDiagnostic(
            ListenerFailedCode,
            LakonaGameDiagnosticSeverity.Error,
            $"RPC listener '{transport}' on '{address}' stopped accepting connections: {Describe(exception)}",
            "Route traffic to another ready node and check the listener error before restarting this node."),
            ExceptionDispatchInfo.Capture(exception)));

    private static string Describe(Exception exception) =>
        string.IsNullOrWhiteSpace(exception.Message) ? exception.GetType().Name : exception.Message;

    public void MarkStopping() => TrySetTerminal(Snapshot.Stopping);

    private bool TrySetTerminal(Snapshot next)
    {
        while (true)
        {
            var current = Volatile.Read(ref snapshot);
            if (current.Status is Status.Failed or Status.Stopping)
            {
                return false;
            }

            if (ReferenceEquals(
                Interlocked.CompareExchange(
                    ref snapshot,
                    next,
                    current),
                current))
            {
                return true;
            }
        }
    }

    private sealed record Snapshot(
        Status Status,
        LakonaGameDiagnostic? Diagnostic = null,
        ExceptionDispatchInfo? ListenerFailure = null)
    {
        public static Snapshot Starting { get; } =
            new(Status.Starting);

        public static Snapshot Ready { get; } =
            new(Status.Ready);

        public static Snapshot Stopping { get; } =
            new(Status.Stopping);
    }

    private enum Status
    {
        Starting,
        Ready,
        Failed,
        Stopping
    }
}
