namespace Lakona.Game.Server.Observability.Diagnostics;

public sealed record DiagnosticsSummaryResponse(
    string Status,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyDictionary<string, object> Sections,
    IReadOnlyList<DiagnosticsProviderError> Errors);

public sealed record DiagnosticsProviderError(
    string Provider,
    string ErrorType,
    string Message);
