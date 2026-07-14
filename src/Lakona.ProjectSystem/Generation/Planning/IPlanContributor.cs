using Lakona.Tool.Domain;

namespace Lakona.Tool.Planning;

internal interface IPlanContributor
{
    void AddFiles(LakonaProjectSpec spec, GenerationPlanBuilder builder);
}
