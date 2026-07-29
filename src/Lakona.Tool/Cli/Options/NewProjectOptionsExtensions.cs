using Lakona.ProjectSystem;

namespace Lakona.Tool.Cli.Options;

internal static class NewProjectOptionsExtensions
{
    public static LakonaProjectCreationRequest ToCreationRequest(this NewProjectOptions options) => new(
        options.ProjectName,
        options.OutputPath,
        options.ClientEngine,
        options.ClientEngineVersion,
        options.Transport,
        options.Serializer,
        options.NuGetForUnitySource,
        options.DeploymentProfile);
}
