namespace Lakona.Game.Server.Guardrails;

public sealed record LakonaGameDiagnostic(
    string Code,
    LakonaGameDiagnosticSeverity Severity,
    string Message,
    string? Repair = null);
