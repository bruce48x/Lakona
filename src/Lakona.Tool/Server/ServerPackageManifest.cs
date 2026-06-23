namespace Lakona.Tool.Server;

internal sealed record ServerPackageManifest
{
    public static ServerPackageManifest CreateV1(
        string version,
        DateTimeOffset builtAtUtc,
        string runtime,
        string configuration,
        string entryAssembly,
        string buildTag,
        string initialHotfixVersion,
        string toolVersion)
    {
        return new ServerPackageManifest(
            version,
            builtAtUtc,
            runtime,
            configuration,
            selfContained: true,
            trimmed: false,
            entryAssembly,
            buildTag,
            initialHotfixVersion,
            toolVersion);
    }

    public ServerPackageManifest(
        string version,
        DateTimeOffset builtAtUtc,
        string runtime,
        string configuration,
        bool selfContained,
        bool trimmed,
        string entryAssembly,
        string buildTag,
        string initialHotfixVersion,
        string toolVersion)
    {
        Version = version;
        BuiltAtUtc = builtAtUtc;
        Runtime = runtime;
        Configuration = configuration;
        SelfContained = selfContained;
        Trimmed = trimmed;
        EntryAssembly = entryAssembly;
        BuildTag = buildTag;
        InitialHotfixVersion = initialHotfixVersion;
        ToolVersion = toolVersion;
    }

    public string Version { get; }

    public DateTimeOffset BuiltAtUtc { get; }

    public string Runtime { get; }

    public string Configuration { get; }

    public bool SelfContained { get; }

    public bool Trimmed { get; }

    public string EntryAssembly { get; }

    public string BuildTag { get; }

    public string InitialHotfixVersion { get; }

    public string ToolVersion { get; }
}
