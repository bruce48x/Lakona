using Lakona.ProjectSystem.Generation.Domain;
using Lakona.ProjectSystem.Generation.Planning;

namespace Lakona.ProjectSystem.Generation.Execution;

internal interface IUnityDependencyRestorer
{
    Task<UnityEditorInstallation> ResolveEditorAsync(
        LakonaProjectSpec spec,
        CancellationToken cancellationToken);

    Task<RestoredUnityDependencies> RestoreAsync(
        GenerationPlan plan,
        UnityEditorInstallation editor,
        CancellationToken cancellationToken);
}
