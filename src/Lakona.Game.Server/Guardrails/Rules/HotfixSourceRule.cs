namespace Lakona.Game.Server.Guardrails.Rules;

internal static class HotfixSourceRule
{
    internal static IEnumerable<LakonaGameDiagnostic> Validate(string assemblyPath)
    {
        if (!File.Exists(assemblyPath))
        {
            yield return new LakonaGameDiagnostic(
                "LAKONA10071",
                LakonaGameDiagnosticSeverity.Error,
                "Hotfix assembly was not found.",
                "dotnet build Server/Hotfix/Server.Hotfix.csproj");
        }
    }
}
