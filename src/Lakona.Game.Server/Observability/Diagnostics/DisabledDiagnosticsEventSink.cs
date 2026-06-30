namespace Lakona.Game.Server.Observability.Diagnostics;

internal sealed class DisabledDiagnosticsEventSink : IDiagnosticsEventSink
{
    public static readonly DisabledDiagnosticsEventSink Instance = new();

    private DisabledDiagnosticsEventSink()
    {
    }

    public void Publish(DiagnosticsEvent diagnosticEvent)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);
    }

    public IReadOnlyList<DiagnosticsEvent> Snapshot(int limit)
    {
        return [];
    }
}
