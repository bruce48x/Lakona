namespace Lakona.Game.Server.Guardrails.Rules;

public sealed class NodeIdentityRule : ILakonaGameValidationRule
{
    public IEnumerable<LakonaGameDiagnostic> Validate(LakonaGameResolvedRuntime runtime)
    {
        if (string.IsNullOrWhiteSpace(runtime.NodeId.Value))
        {
            yield return new LakonaGameDiagnostic(
                "ULINK001",
                LakonaGameDiagnosticSeverity.Error,
                "Node id is required.",
                "Set Lakona.Game:Node:Id to a stable node id.");
        }
    }
}
