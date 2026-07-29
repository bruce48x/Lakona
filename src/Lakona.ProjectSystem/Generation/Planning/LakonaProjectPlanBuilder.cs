using Lakona.ProjectSystem.Generation.Domain;
using Lakona.ProjectSystem.Generation.Rendering.Client;

namespace Lakona.ProjectSystem.Generation.Planning;

internal sealed class LakonaProjectPlanBuilder(
    IReadOnlyList<IPlanContributor> contributors,
    IReadOnlyList<IClientRenderer>? clientRenderers = null)
{
    public GenerationPlan Build(LakonaProjectSpec spec)
    {
        var builder = new GenerationPlanBuilder(spec.Layout.RootPath);
        foreach (var contributor in contributors)
        {
            contributor.AddFiles(spec, builder);
        }

        var selectedClientRenderer = clientRenderers?.SingleOrDefault(renderer => renderer.Supports(spec.ClientEngine));
        selectedClientRenderer?.AddFiles(spec, builder);

        return PlanValidator.Validate(builder.Build());
    }
}
