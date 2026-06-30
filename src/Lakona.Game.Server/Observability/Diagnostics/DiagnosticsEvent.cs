using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Observability.Diagnostics;

public sealed record DiagnosticsEvent(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Category,
    string Kind,
    string Message,
    string? TraceId,
    string? CorrelationId,
    IReadOnlyDictionary<string, string?> Dimensions);
