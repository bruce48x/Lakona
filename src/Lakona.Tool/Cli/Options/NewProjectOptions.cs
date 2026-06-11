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
    Persistence = 1 << 5,
    NuGetForUnitySource = 1 << 6,
    DeployProfile = 1 << 7
}

internal readonly record struct NewProjectOptions(
    string? ProjectName,
    string? OutputPath,
    ClientEngine ClientEngine,
    TransportKind Transport,
    SerializerKind Serializer,
    PersistenceKind Persistence,
    NuGetForUnitySource NuGetForUnitySource,
    DeploymentProfile DeploymentProfile,
    NewProjectOptionPresence Presence = NewProjectOptionPresence.None)
{
    public bool HasExplicit(NewProjectOptionPresence option)
    {
        return (Presence & option) == option;
    }
}
