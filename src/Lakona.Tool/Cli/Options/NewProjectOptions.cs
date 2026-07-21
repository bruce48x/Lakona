using Lakona.Tool.Domain;

namespace Lakona.Tool.Cli.Options;

[Flags]
internal enum NewProjectOptionPresence
{
    None = 0,
    Name = 1 << 0,
    OutputPath = 1 << 1,
    ClientEngine = 1 << 2,
    Transport = 1 << 3,
    Serializer = 1 << 4,
    NuGetForUnitySource = 1 << 5,
    DeployProfile = 1 << 6,
    ClientEngineVersion = 1 << 7
}

internal readonly record struct NewProjectOptions(
    string? ProjectName,
    string? OutputPath,
    ClientEngine ClientEngine,
    TransportKind Transport,
    SerializerKind Serializer,
    NuGetForUnitySource NuGetForUnitySource,
    DeploymentProfile DeploymentProfile,
    NewProjectOptionPresence Presence = NewProjectOptionPresence.None,
    ClientEngineVersion? ClientEngineVersion = null)
{
    public bool HasExplicit(NewProjectOptionPresence option)
    {
        return (Presence & option) == option;
    }
}
