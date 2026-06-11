using Lakona.Tool.Domain;
using Lakona.Tool.Execution;

namespace Lakona.Tool.Planning;

internal sealed class LakonaProjectGenerator(
    LakonaProjectPlanBuilder planBuilder,
    GenerationExecutor executor)
{
    public async Task GenerateAsync(LakonaProjectSpec spec, CancellationToken cancellationToken)
    {
        var plan = planBuilder.Build(spec);
        await executor.ExecuteAsync(plan, cancellationToken).ConfigureAwait(false);
    }
}
