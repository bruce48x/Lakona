using System.Diagnostics.Metrics;
using Lakona.Game.Server.Observability;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Sessions;

internal static class ClientNotificationDiagnostics
{
    private static readonly Meter Meter = new(
        LakonaGameServerTelemetry.SessionMeterName,
        typeof(ClientNotificationDiagnostics).Assembly.GetName().Version?.ToString());
    private static readonly Counter<long> BackpressureCounter = Meter.CreateCounter<long>(
        "lakona.game.notification.backpressure");

    internal static readonly Counter<long> SendFailures = Meter.CreateCounter<long>(
        "lakona.game.notification.send_failure");

    internal static void LogFailure(
        ILogger? logger, TimeProvider clock, ref long nextWarningAt,
        string stage, GameSessionKey session, Exception exception,
        int? serviceId = null, int? methodId = null)
    {
        if (logger is null || !logger.IsEnabled(LogLevel.Warning)) return;
        // One window per owning boundary, across all sessions. Do not retain
        // exception data: serializer messages and stacks can contain payloads.
        var now = clock.GetTimestamp();
        var next = Volatile.Read(ref nextWarningAt);
        if (now < next || Interlocked.CompareExchange(
                ref nextWarningAt, now + 30 * clock.TimestampFrequency, next) != next)
            return;

        logger.LogWarning(
            "Client notification failed. Stage={Stage} SessionId={SessionId} ServiceId={ServiceId} MethodId={MethodId} ExceptionType={ExceptionType}. Further failure warnings at this boundary suppressed for 30 seconds.",
            stage, session.SessionId, serviceId, methodId, exception.GetType().FullName);
    }

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
                    ClientNotificationBackpressureReason.PendingBytes => "pending_bytes",
                    _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
                }));
}

internal enum ClientNotificationBackpressureReason
{
    SessionCapacity,
    ProcessCapacity,
    BatchBytes,
    PendingBytes
}
