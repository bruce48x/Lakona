namespace Lakona.Game.Server.Guardrails;

public sealed class LakonaGameRuntimeValidator
{
    private readonly IReadOnlyList<ILakonaGameValidationRule> _rules;

    public LakonaGameRuntimeValidator(IEnumerable<ILakonaGameValidationRule> rules)
    {
        _rules = rules?.ToArray() ?? throw new ArgumentNullException(nameof(rules));
    }

    public LakonaGameValidationResult Validate(LakonaGameResolvedRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        var diagnostics = new List<LakonaGameDiagnostic>();
        foreach (var rule in _rules)
        {
            diagnostics.AddRange(rule.Validate(runtime));
        }

        return new LakonaGameValidationResult(diagnostics);
    }
}
