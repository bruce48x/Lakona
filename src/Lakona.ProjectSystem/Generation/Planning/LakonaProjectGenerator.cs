using Lakona.ProjectSystem.Generation.Domain;
using Lakona.ProjectSystem.Generation.Execution;

namespace Lakona.ProjectSystem.Generation.Planning;

internal sealed class LakonaProjectGenerator(
    LakonaProjectPlanBuilder planBuilder,
    GenerationExecutor executor,
    GitInitializer gitInitializer,
    IUnityDependencyRestorer? unityDependencyRestorer = null)
{
    public async Task<LakonaProjectGenerationResult> GenerateAsync(
        LakonaProjectSpec spec,
        CancellationToken cancellationToken)
    {
        var plan = planBuilder.Build(spec);
        using var restoredDependencies = unityDependencyRestorer is null
            ? null
            : await unityDependencyRestorer.RestoreAsync(spec, plan, cancellationToken).ConfigureAwait(false);
        await executor.ExecuteAsync(plan, restoredDependencies?.RootPath, cancellationToken).ConfigureAwait(false);

        var gitResult = await gitInitializer.InitializeAsync(plan.RootPath, cancellationToken)
            .ConfigureAwait(false);

        return new LakonaProjectGenerationResult(plan.RootPath, gitResult);
    }
}
