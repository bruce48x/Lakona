using System.Diagnostics.Metrics;
using Lakona.Game.Server.Observability;

namespace Lakona.Game.Server.Sessions;

internal static class ClientNotificationDiagnostics
{
    private static readonly Meter Meter = new(
        LakonaGameServerTelemetry.SessionMeterName,
        typeof(ClientNotificationDiagnostics).Assembly.GetName().Version?.ToString());
    private static readonly Counter<long> BackpressureCounter = Meter.CreateCounter<long>(
        "lakona.game.notification.backpressure");

    internal static void RecordBackpressure(ClientNotificationBackpressureReason reason) =>
        BackpressureCounter.Add(
            1,
            new KeyValuePair<string, object?>(
                "lakona.game.notification.reason",
                reason switch
                {
                    ClientNotificationBackpressureReason.SessionCapacity => "session_capacity",
                    ClientNotificationBackpressureReason.ProcessCapacity => "process_capacity",
                    ClientNotificationBackpressureReason.BatchBytes => "batch_bytes",
                    _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
                }));
}

internal enum ClientNotificationBackpressureReason
{
    SessionCapacity,
    ProcessCapacity,
    BatchBytes
}
