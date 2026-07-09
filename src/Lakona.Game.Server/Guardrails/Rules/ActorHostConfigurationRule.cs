namespace Lakona.Game.Server.Guardrails.Rules;

public sealed class ActorHostConfigurationRule : ILakonaGameValidationRule
{
    public IEnumerable<LakonaGameDiagnostic> Validate(LakonaGameResolvedRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        foreach (var diagnostic in ValidateNames(
            runtime.ActorHosts ?? [],
            blankCode: "ULINK101",
            duplicateCode: "ULINK102",
            blankMessage: "Lakona:ActorHosts entries must not be empty.",
            duplicateMessage: "Lakona:ActorHosts entries must be unique."))
        {
            yield return diagnostic;
        }

        foreach (var diagnostic in ValidateNames(
            (runtime.StartupActors ?? []).Select(static actor => actor.Name),
            blankCode: "ULINK103",
            duplicateCode: "ULINK104",
            blankMessage: "Lakona:StartupActors entries must not be empty.",
            duplicateMessage: "Lakona:StartupActors entries must be unique."))
        {
            yield return diagnostic;
        }
    }

    private static IEnumerable<LakonaGameDiagnostic> ValidateNames(
        IEnumerable<LakonaGameResolvedValue<string>> values,
        string blankCode,
        string duplicateCode,
        string blankMessage,
        string duplicateMessage)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value.Value))
            {
                yield return Error(blankCode, blankMessage, value.Path);
                continue;
            }

            if (!seen.Add(value.Value))
            {
                yield return Error(duplicateCode, duplicateMessage, value.Path);
            }
        }
    }

    private static LakonaGameDiagnostic Error(string code, string message, string? path)
    {
        return new LakonaGameDiagnostic(
            code,
            LakonaGameDiagnosticSeverity.Error,
            string.IsNullOrWhiteSpace(path) ? message : $"{path}: {message}");
    }
}
