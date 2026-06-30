using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Observability.Diagnostics;

public sealed class BoundedDiagnosticsEventBuffer : IDiagnosticsEventSink
{
    private static readonly HashSet<string> SensitiveDimensionKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "actor_id",
        "actor_name",
        "session_id",
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
            Category = Limit(diagnosticEvent.Category, 160),
            Kind = Limit(diagnosticEvent.Kind, 80),
            Message = Limit(diagnosticEvent.Message, 240),
            TraceId = LimitNullable(diagnosticEvent.TraceId, 64),
            CorrelationId = LimitNullable(diagnosticEvent.CorrelationId, 64),
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

            sanitized[Limit(key, 80)] = LimitNullable(value, 160);
        }

        return sanitized;
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
