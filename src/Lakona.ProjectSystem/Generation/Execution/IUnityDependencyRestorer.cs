using Lakona.ProjectSystem.Generation.Domain;
using Lakona.ProjectSystem.Generation.Planning;

namespace Lakona.ProjectSystem.Generation.Execution;

internal interface IUnityDependencyRestorer
{
    Task<RestoredUnityDependencies?> RestoreAsync(
        LakonaProjectSpec spec,
        GenerationPlan plan,
        CancellationToken cancellationToken);
}

