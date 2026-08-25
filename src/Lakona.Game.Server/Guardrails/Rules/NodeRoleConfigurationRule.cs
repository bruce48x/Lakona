namespace Lakona.Game.Server.Guardrails.Rules;

public sealed class NodeRoleConfigurationRule : ILakonaGameValidationRule
{
    public IEnumerable<LakonaGameDiagnostic> Validate(LakonaGameResolvedRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in runtime.NodeRoles ?? [])
        {
            if (string.IsNullOrWhiteSpace(value.Value))
            {
                yield return Error("LAKONA101", "Lakona:Node:Roles entries must not be empty.", value.Path);
                continue;
            }

            if (!seen.Add(value.Value))
            {
                yield return Error("LAKONA102", "Lakona:Node:Roles entries must be unique.", value.Path);
            }
        }
    }

    private static LakonaGameDiagnostic Error(string code, string message, string? path) =>
        new(
            code,
            LakonaGameDiagnosticSeverity.Error,
            string.IsNullOrWhiteSpace(path) ? message : $"{path}: {message}");
}
