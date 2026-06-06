namespace Lakona.Game.Server.Guardrails.Rules;

public sealed class ClusterServiceGraphRule : ILakonaGameValidationRule
{
    public IEnumerable<LakonaGameDiagnostic> Validate(LakonaGameResolvedRuntime runtime)
    {
        var duplicated = runtime.Cluster.Services
            .GroupBy(service => service.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicated is not null)
        {
            yield return new LakonaGameDiagnostic(
                "ULINK041",
                LakonaGameDiagnosticSeverity.Error,
                $"Cluster service name '{duplicated.Key}' is duplicated.",
                "Use unique service names in the resolved cluster service list.");
        }
    }
}
