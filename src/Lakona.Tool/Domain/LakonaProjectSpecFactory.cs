using Lakona.Tool.Cli.Options;

namespace Lakona.Tool.Domain;

internal sealed class LakonaProjectSpecFactory
{
    public LakonaProjectSpec Create(NewProjectOptions options)
    {
        var projectName = string.IsNullOrWhiteSpace(options.ProjectName) ? "MyGame" : options.ProjectName;
        var layout = ProjectLayout.Create(projectName, options.OutputPath);

        return new LakonaProjectSpec(
            projectName,
            layout,
            options.ClientEngine,
            options.Transport,
            options.Serializer,
            options.Persistence,
            options.NuGetForUnitySource,
            options.DeploymentProfile,
            ProjectFeatureCatalog.DefaultFeatures);
    }
}
