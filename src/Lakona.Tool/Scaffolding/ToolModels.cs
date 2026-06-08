internal static class ProjectConventions
{
    public const string ConfigFileName = "lakona-game.tool.json";
    public const string DefaultProjectName = "MyGame";
    public const string DefaultClientEngine = "unity";
    public const string DefaultTransport = "kcp";
    public const string DefaultNetworkProfile = "cluster";
    public const string DefaultSerializer = "memorypack";
    public const string DefaultPersistence = "none";
    public const string DefaultNuGetForUnitySource = "openupm";
    public const string DefaultDeployProfile = "none";
    public const string StarterServerProjectPath = "Server/Server";
    public const string StarterServerGeneratedNamespace = "Server.Generated";

    public static readonly string[] SupportedClientEngines = ["unity", "unity-cn", "tuanjie", "godot"];
    public static readonly string[] SupportedTransports = ["tcp", "websocket", "kcp"];
    public static readonly string[] SupportedNetworkProfiles = ["cluster"];
    public static readonly string[] SupportedSerializers = ["json", "memorypack"];
    public static readonly string[] SupportedPersistence = ["none", "mysql", "postgres"];
    public static readonly string[] SupportedNuGetForUnitySources = ["embedded", "openupm"];
    public static readonly string[] SupportedDeployProfiles = ["none", "compose"];

    public static bool IsGodot(string clientEngine)
    {
        return string.Equals(clientEngine, "godot", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsRealtimeNetworkProfile(string networkProfile)
    {
        return string.Equals(networkProfile, "realtime", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsClusterNetworkProfile(string networkProfile)
    {
        return string.Equals(networkProfile, "cluster", StringComparison.OrdinalIgnoreCase);
    }

    public static bool UsesExternalPersistence(string persistence)
    {
        return !string.Equals(persistence, DefaultPersistence, StringComparison.OrdinalIgnoreCase);
    }

    public static bool UsesComposeDeployProfile(string deployProfile)
    {
        return string.Equals(deployProfile, "compose", StringComparison.OrdinalIgnoreCase);
    }
}

internal static partial class ToolPackageVersions
{
    public const string MicrosoftExtensionsHosting = "10.0.0";
    public const string Dapper = "2.1.72";
    public const string MySqlConnector = "2.5.0";
    public const string Npgsql = "10.0.2";
}

internal sealed class ToolConfig
{
    public ProjectConfig Project { get; set; } = new();

    public static ToolConfig CreateDefault(string projectName, NewCommandOptions options)
    {
        return new ToolConfig
        {
            Project = new ProjectConfig
            {
                Name = projectName,
                ClientEngine = options.ClientEngine,
                Transport = options.Transport,
                NetworkProfile = options.NetworkProfile,
                Serializer = options.Serializer,
                Persistence = options.Persistence,
                NuGetForUnitySource = options.NuGetForUnitySource,
                DeployProfile = options.DeployProfile
            }
        };
    }
}

internal sealed class ProjectConfig
{
    public string Name { get; set; } = ProjectConventions.DefaultProjectName;
    public string ClientEngine { get; set; } = ProjectConventions.DefaultClientEngine;
    public string Transport { get; set; } = ProjectConventions.DefaultTransport;
    public string NetworkProfile { get; set; } = ProjectConventions.DefaultNetworkProfile;
    public string Serializer { get; set; } = ProjectConventions.DefaultSerializer;
    public string Persistence { get; set; } = ProjectConventions.DefaultPersistence;
    public string NuGetForUnitySource { get; set; } = ProjectConventions.DefaultNuGetForUnitySource;
    public string DeployProfile { get; set; } = ProjectConventions.DefaultDeployProfile;
}

[Flags]
internal enum NewCommandOptionPresence
{
    None = 0,
    Name = 1 << 0,
    OutputPath = 1 << 1,
    ClientEngine = 1 << 2,
    Transport = 1 << 3,
    NetworkProfile = 1 << 4,
    Serializer = 1 << 5,
    Persistence = 1 << 6,
    NuGetForUnitySource = 1 << 7,
    DeployProfile = 1 << 8
}

internal readonly record struct NewCommandOptions(
    string? Name,
    string? OutputPath,
    string ClientEngine,
    string Transport,
    string NetworkProfile,
    string Serializer,
    string Persistence,
    string NuGetForUnitySource,
    string DeployProfile,
    NewCommandOptionPresence Presence = NewCommandOptionPresence.None)
{
    public bool HasExplicit(NewCommandOptionPresence option)
    {
        return (Presence & option) == option;
    }
}

internal readonly record struct PackageArtifact(string PackageId, string Version, string Namespace);

internal sealed class CliUsageException(string message) : Exception(message);
