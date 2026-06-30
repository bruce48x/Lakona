using System.Diagnostics;
using System.Globalization;
using Lakona.Game.Server.Actors;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Observability.Diagnostics;

public sealed class ActorDiagnosticsEventBridge : IActorDiagnosticsObserver
{
    private const string Category = "Lakona.Game.Actor";
    private readonly IDiagnosticsEventSink _sink;

    public ActorDiagnosticsEventBridge(IDiagnosticsEventSink sink)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
    }

    public void OnDeadLetter(ActorDeadLetterDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        Publish(
            LogLevel.Warning,
            "actor.dead_letter",
            "Actor dead letter observed.",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["message_type"] = TypeName(diagnostic.Message),
                ["reason"] = LowCardinality(diagnostic.Reason)
            });
    }

    public void OnSlowMessage(ActorSlowMessageDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        Publish(
            LogLevel.Warning,
            "actor.slow_message",
            "Actor slow message observed.",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["message_type"] = TypeName(diagnostic.Message),
                ["elapsed_ms"] = Milliseconds(diagnostic.Elapsed)
            });
    }

    public void OnCallTimeout(ActorCallTimeoutDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        Publish(
            LogLevel.Error,
            "actor.call_timeout",
            "Actor call timeout observed.",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["request_type"] = TypeName(diagnostic.Request),
                ["reason"] = diagnostic.Reason.ToString(),
                ["timeout_ms"] = Milliseconds(diagnostic.Timeout)
            });
    }

    private void Publish(
        LogLevel level,
        string kind,
        string message,
        IReadOnlyDictionary<string, string?> dimensions)
    {
        var activity = Activity.Current;
        _sink.Publish(new DiagnosticsEvent(
            DateTimeOffset.UtcNow,
            level,
            Category,
            kind,
            message,
            activity?.TraceId.ToString(),
            activity?.SpanId.ToString(),
            dimensions));
    }

    private static string TypeName(object value)
    {
        return LowCardinality(value.GetType().Name);
    }

    private static string LowCardinality(string value)
    {
        return value.Length <= 80 ? value : value[..80];
    }

    private static string Milliseconds(TimeSpan value)
    {
        return Math.Max(0, (long)value.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);
    }
}
