using Lakona.ProjectSystem.Generation.Domain;

namespace Lakona.ProjectSystem.Generation.Planning;

internal interface IPlanContributor
{
    void AddFiles(LakonaProjectSpec spec, GenerationPlanBuilder builder);
}
