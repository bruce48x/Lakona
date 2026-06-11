using Lakona.Tool.Planning;

namespace Lakona.Tool.Execution;

internal sealed class GenerationExecutor(TransactionalOutputWriter writer)
{
    public async Task ExecuteAsync(GenerationPlan plan, CancellationToken cancellationToken)
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

        await writer.WriteAsync(validatedPlan, cancellationToken).ConfigureAwait(false);
    }
}
