using Lakona.ProjectSystem.Generation.Domain;

namespace Lakona.ProjectSystem.Tests;

[Flags]
internal enum ProjectSpecTestOptionPresence
{
    None = 0,
    NuGetForUnitySource = 1 << 0
}

internal readonly record struct ProjectSpecTestOptions(
    string? ProjectName,
    string? OutputPath,
    ClientEngine ClientEngine,
    TransportKind Transport,
    SerializerKind Serializer,
    NuGetForUnitySource NuGetForUnitySource,
    DeploymentProfile DeploymentProfile,
    ProjectSpecTestOptionPresence Presence = ProjectSpecTestOptionPresence.None,
    ClientEngineVersion? ClientEngineVersion = null,
    MembershipProviderKind MembershipProvider = MembershipProviderKind.Memory);

internal sealed class ProjectSpecTestFactory
{
    public LakonaProjectSpec Create(ProjectSpecTestOptions options)
    {
        return new ProjectSpecFactory().Create(new LakonaProjectCreationRequest(
            options.ProjectName,
            options.OutputPath,
            (LakonaClientEngine)options.ClientEngine,
            options.ClientEngineVersion is { } version
                ? (LakonaClientEngineVersion)version
                : null,
            (LakonaTransport)options.Transport,
            (LakonaSerializer)options.Serializer,
            (LakonaNuGetForUnitySource)options.NuGetForUnitySource,
            (LakonaDeploymentProfile)options.DeploymentProfile,
            (LakonaMembershipProvider)options.MembershipProvider));
    }
}
