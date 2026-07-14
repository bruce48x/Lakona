using Lakona.ProjectSystem;

namespace Lakona.Tool.Cli.Options;

internal static class NewProjectOptionsExtensions
{
    public static LakonaProjectCreationRequest ToCreationRequest(this NewProjectOptions options) => new(
        options.ProjectName,
        options.OutputPath,
        (LakonaClientEngine)options.ClientEngine,
        options.ClientEngineVersion is { } version ? (LakonaClientEngineVersion)version : null,
        (LakonaTransport)options.Transport,
        (LakonaSerializer)options.Serializer,
        (LakonaPersistence)options.Persistence,
        (LakonaNuGetForUnitySource)options.NuGetForUnitySource,
        (LakonaDeploymentProfile)options.DeploymentProfile);
}
