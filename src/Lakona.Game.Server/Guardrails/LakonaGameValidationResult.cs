namespace Lakona.Game.Server.Guardrails;

public sealed record LakonaGameValidationResult(
    IReadOnlyList<LakonaGameDiagnostic> Diagnostics)
{
    public bool Succeeded => Diagnostics.All(static diagnostic =>
        diagnostic.Severity != LakonaGameDiagnosticSeverity.Error);

    public static LakonaGameValidationResult Success { get; } = new([]);
}
