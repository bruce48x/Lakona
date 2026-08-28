using Lakona.ProjectSystem.Generation.Domain;
using Lakona.ProjectSystem.Generation.Execution;

namespace Lakona.ProjectSystem.Generation.Planning;

internal sealed class LakonaProjectGenerator(
    LakonaProjectPlanBuilder planBuilder,
    GenerationExecutor executor,
    GitInitializer gitInitializer,
    IUnityDependencyRestorer? unityDependencyRestorer = null)
{
    public Task<LakonaProjectGenerationResult> GenerateAsync(
        LakonaProjectSpec spec,
        CancellationToken cancellationToken) =>
        GenerateAsync(spec, progress: null, cancellationToken);

    public async Task<LakonaProjectGenerationResult> GenerateAsync(
        LakonaProjectSpec spec,
        IProgress<LakonaProjectCreationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var plan = planBuilder.Build(spec);
        if (ClientEnginePolicy.IsUnityCompatible(spec.ClientEngine))
        {
            progress?.Report(new LakonaProjectCreationProgress(LakonaProjectCreationStage.RestoringClientDependencies));
        }
        using var restoredDependencies = unityDependencyRestorer is null
            ? null
            : await unityDependencyRestorer.RestoreAsync(spec, plan, cancellationToken).ConfigureAwait(false);
        progress?.Report(new LakonaProjectCreationProgress(LakonaProjectCreationStage.WritingProject));
        await executor.ExecuteAsync(plan, restoredDependencies?.RootPath, cancellationToken).ConfigureAwait(false);

        progress?.Report(new LakonaProjectCreationProgress(LakonaProjectCreationStage.InitializingGit));
        var gitResult = await gitInitializer.InitializeAsync(plan.RootPath, cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(new LakonaProjectCreationProgress(LakonaProjectCreationStage.Completed));
        return new LakonaProjectGenerationResult(plan.RootPath, gitResult);
    }
}
