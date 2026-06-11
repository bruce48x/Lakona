using Lakona.Tool.Domain;
using Lakona.Tool.Rendering.Client;

namespace Lakona.Tool.Planning;

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
