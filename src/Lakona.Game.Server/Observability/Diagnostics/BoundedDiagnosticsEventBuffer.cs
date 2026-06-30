using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Observability.Diagnostics;

public sealed class BoundedDiagnosticsEventBuffer : IDiagnosticsEventSink
{
    private static readonly Regex WindowsPathPattern = new(
        @"[A-Za-z]:\\[^\s""']+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex UnixPathPattern = new(
        @"/(?:home|Users|var|tmp|etc|opt|srv|deploy)/[^\s""']+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BearerTokenPattern = new(
        @"\bBearer\s+[A-Za-z0-9._~+/\-]+=*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex BracedSensitiveValuePattern = new(
        @"\b(?:payload|request)\s+\{[^}\r\n]*\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex SeparatedSensitiveValuePattern = new(
        @"\b(?:token|password|secret|api[-_]?key|request|payload)\s*[:=]\s*(?:\{[^}\r\n]*\}|[^\s""',;}]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex SpacedSensitiveValuePattern = new(
        @"\b(?:token|password|secret|api[-_]?key)\s+[^\s""',;}]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex HyphenatedSensitiveValuePattern = new(
        @"\b(?:token|password|secret|api[-_]?key)[-_][^\s""',;}]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> SensitiveDimensionKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "actor_id",
        "actor_name",
        "session_id",
        "connection_id",
        "token",
        "payload",
        "request",
        "request_value",
        "user_id",
        "call_chain",
        "correlation_chain"
    };

    private readonly object _gate = new();
    private readonly Queue<DiagnosticsEvent> _events;

    public BoundedDiagnosticsEventBuffer(int capacity, LogLevel minimumLevel)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be greater than zero.");
        }

        Capacity = capacity;
        MinimumLevel = minimumLevel;
        _events = new Queue<DiagnosticsEvent>(capacity);
    }

    public int Capacity { get; }

    public LogLevel MinimumLevel { get; }

    public void Publish(DiagnosticsEvent diagnosticEvent)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);

        if (diagnosticEvent.Level < MinimumLevel || diagnosticEvent.Level == LogLevel.None)
        {
            return;
        }

        var sanitized = diagnosticEvent with
        {
            Category = Limit(SanitizeMessage(diagnosticEvent.Category), 160),
            Kind = Limit(SanitizeMessage(diagnosticEvent.Kind), 80),
            Message = Limit(SanitizeMessage(diagnosticEvent.Message), 240),
            TraceId = LimitNullable(SanitizeNullable(diagnosticEvent.TraceId), 64),
            CorrelationId = LimitNullable(SanitizeNullable(diagnosticEvent.CorrelationId), 64),
            Dimensions = SanitizeDimensions(diagnosticEvent.Dimensions)
        };

        lock (_gate)
        {
            while (_events.Count >= Capacity)
            {
                _events.Dequeue();
            }

            _events.Enqueue(sanitized);
        }
    }

    public IReadOnlyList<DiagnosticsEvent> Snapshot(int limit)
    {
        if (limit <= 0)
        {
            return [];
        }

        lock (_gate)
        {
            return _events
                .Reverse()
                .Take(limit)
                .ToArray();
        }
    }

    private static IReadOnlyDictionary<string, string?> SanitizeDimensions(
        IReadOnlyDictionary<string, string?> dimensions)
    {
        if (dimensions.Count == 0)
        {
            return new Dictionary<string, string?>(0, StringComparer.Ordinal);
        }

        var sanitized = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, value) in dimensions)
        {
            if (string.IsNullOrWhiteSpace(key) || SensitiveDimensionKeys.Contains(key))
            {
                continue;
            }

            sanitized[Limit(key, 80)] = LimitNullable(SanitizeNullable(value), 160);
        }

        return sanitized;
    }

    private static string SanitizeMessage(string message)
    {
        var sanitized = WindowsPathPattern.Replace(message, "[redacted-path]");
        sanitized = UnixPathPattern.Replace(sanitized, "[redacted-path]");
        sanitized = BearerTokenPattern.Replace(sanitized, "Bearer [redacted]");
        sanitized = BracedSensitiveValuePattern.Replace(sanitized, "[redacted]");
        sanitized = SeparatedSensitiveValuePattern.Replace(sanitized, "[redacted]");
        sanitized = SpacedSensitiveValuePattern.Replace(sanitized, "[redacted]");
        sanitized = HyphenatedSensitiveValuePattern.Replace(sanitized, "[redacted]");
        return sanitized;
    }

    private static string? SanitizeNullable(string? value)
    {
        return value is null ? null : SanitizeMessage(value);
    }

    private static string Limit(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static string? LimitNullable(string? value, int maxLength)
    {
        return value is null ? null : Limit(value, maxLength);
    }
}
