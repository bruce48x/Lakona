using Lakona.Tool.Cli.Options;

namespace Lakona.Tool.Domain;

internal sealed class LakonaProjectSpecFactory
{
    public LakonaProjectSpec Create(NewProjectOptions options)
    {
        var projectName = string.IsNullOrWhiteSpace(options.ProjectName) ? "MyGame" : options.ProjectName;
        var layout = ProjectLayout.Create(projectName, options.OutputPath);
        var nuGetForUnitySource = ClientEnginePolicy.GetEffectiveNuGetForUnitySource(
            options.ClientEngine,
            options.NuGetForUnitySource);

        return new LakonaProjectSpec(
            projectName,
            layout,
            options.ClientEngine,
            options.Transport,
            options.Serializer,
            options.Persistence,
            nuGetForUnitySource,
            options.DeploymentProfile,
            ProjectFeatureCatalog.DefaultFeatures);
    }
}
