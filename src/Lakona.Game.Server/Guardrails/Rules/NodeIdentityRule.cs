namespace Lakona.Game.Server.Guardrails.Rules;

internal static class NodeIdentityRule
{
    internal static IEnumerable<LakonaGameDiagnostic> Validate(string nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            yield return new LakonaGameDiagnostic(
                "LAKONA10001",
                LakonaGameDiagnosticSeverity.Error,
                "Node id is required.",
                "Set Lakona:Node:Id to a stable node id.");
        }
    }
}
