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
        UnityEditorInstallation? editor = null;
        if (ClientEnginePolicy.IsUnityCompatible(spec.ClientEngine))
        {
            progress?.Report(new LakonaProjectCreationProgress(LakonaProjectCreationStage.RestoringClientDependencies));
            if (unityDependencyRestorer is not null)
            {
                editor = await unityDependencyRestorer.ResolveEditorAsync(spec, cancellationToken).ConfigureAwait(false);
                spec = spec with
                {
                    ClientEditorVersion = editor.Version,
                    ClientEditorRevision = editor.Revision
                };
            }
        }

        var plan = planBuilder.Build(spec);
        using var restoredDependencies = unityDependencyRestorer is null || editor is null
            ? null
            : await unityDependencyRestorer.RestoreAsync(plan, editor, cancellationToken).ConfigureAwait(false);
        progress?.Report(new LakonaProjectCreationProgress(LakonaProjectCreationStage.WritingProject));
        await executor.ExecuteAsync(plan, restoredDependencies?.RootPath, cancellationToken).ConfigureAwait(false);

        progress?.Report(new LakonaProjectCreationProgress(LakonaProjectCreationStage.InitializingGit));
        var gitResult = await gitInitializer.InitializeAsync(plan.RootPath, cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(new LakonaProjectCreationProgress(LakonaProjectCreationStage.Completed));
        return new LakonaProjectGenerationResult(plan.RootPath, gitResult);
    }
}
