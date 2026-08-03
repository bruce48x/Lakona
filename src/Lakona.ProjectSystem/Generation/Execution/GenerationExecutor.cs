using Lakona.ProjectSystem.Generation.Planning;

namespace Lakona.ProjectSystem.Generation.Execution;

internal sealed class GenerationExecutor(TransactionalOutputWriter writer)
{
    public Task ExecuteAsync(GenerationPlan plan, CancellationToken cancellationToken) =>
        ExecuteAsync(plan, restoredUnityPackagesPath: null, cancellationToken);

    public async Task ExecuteAsync(GenerationPlan plan, string? restoredUnityPackagesPath, CancellationToken cancellationToken)
    {
        var validatedPlan = PlanValidator.Validate(plan);
        var errors = validatedPlan.Diagnostics
            .Where(diagnostic => diagnostic.Severity == PlanDiagnosticSeverity.Error)
            .ToArray();
        if (errors.Length > 0)
        {
            var message = string.Join(
                Environment.NewLine,
                errors.Select(error => $"{error.Code}: {error.Message}"));
            throw new InvalidOperationException(message);
        }

        await writer.WriteAsync(validatedPlan, restoredUnityPackagesPath, cancellationToken).ConfigureAwait(false);
    }
}
