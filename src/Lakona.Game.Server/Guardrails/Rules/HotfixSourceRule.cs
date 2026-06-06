namespace Lakona.Game.Server.Guardrails.Rules;

public sealed class HotfixSourceRule : ILakonaGameValidationRule
{
    public IEnumerable<LakonaGameDiagnostic> Validate(LakonaGameResolvedRuntime runtime)
    {
        if (!File.Exists(runtime.Hotfix.AssemblyPath.Value))
        {
            yield return new LakonaGameDiagnostic(
                "ULINK071",
                LakonaGameDiagnosticSeverity.Error,
                "Hotfix assembly was not found.",
                "dotnet build Server/Hotfix/Server.Hotfix.csproj");
        }
    }
}
