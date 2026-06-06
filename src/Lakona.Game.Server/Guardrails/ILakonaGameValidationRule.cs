namespace Lakona.Game.Server.Guardrails;

public interface ILakonaGameValidationRule
{
    IEnumerable<LakonaGameDiagnostic> Validate(LakonaGameResolvedRuntime runtime);
}
