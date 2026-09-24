using Lakona.Game.Server.Configuration;

namespace Lakona.Game.Server.Guardrails.Rules;

internal static class NodeRoleConfigurationRule
{
    internal static IEnumerable<LakonaGameDiagnostic> Validate(LakonaGameRuntimeOptions runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < runtime.Node.Roles.Count; index++)
        {
            var value = runtime.Node.Roles[index];
            var path = $"Lakona:Node:Roles:{index}";
            if (string.IsNullOrWhiteSpace(value))
            {
                yield return Error("LAKONA10101", "Lakona:Node:Roles entries must not be empty.", path);
                continue;
            }

            if (!seen.Add(value))
            {
                yield return Error("LAKONA10102", "Lakona:Node:Roles entries must be unique.", path);
            }
        }
    }

    private static LakonaGameDiagnostic Error(string code, string message, string? path) =>
        new(
            code,
            LakonaGameDiagnosticSeverity.Error,
            string.IsNullOrWhiteSpace(path) ? message : $"{path}: {message}");
}
