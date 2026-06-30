namespace Lakona.Game.Server.Observability.Diagnostics;

public interface IDiagnosticsEventSink
{
    void Publish(DiagnosticsEvent diagnosticEvent);

    IReadOnlyList<DiagnosticsEvent> Snapshot(int limit);
}
