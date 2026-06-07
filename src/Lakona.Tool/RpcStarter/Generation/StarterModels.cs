namespace Lakona.Tool.RpcStarter;

internal enum TransportKind
{
    Tcp,
    WebSocket,
    Kcp
}

internal enum SerializerKind
{
    Json,
    MemoryPack
}

internal enum ClientEngineKind
{
    Unity,
    UnityCn,
    Tuanjie,
    Godot,
    Console
}

internal enum NuGetForUnitySourceKind
{
    Embedded,
    OpenUpm
}

internal static class ClientEngineKindExtensions
{
    public static bool IsUnityCompatible(this ClientEngineKind clientEngine) => clientEngine switch
    {
        ClientEngineKind.Unity => true,
        ClientEngineKind.UnityCn => true,
        ClientEngineKind.Tuanjie => true,
        ClientEngineKind.Godot => false,
        ClientEngineKind.Console => false,
        _ => throw new ArgumentOutOfRangeException(nameof(clientEngine), clientEngine, null)
    };

    public static string GetDisplayName(this ClientEngineKind clientEngine) => clientEngine switch
    {
        ClientEngineKind.Unity => "Unity",
        ClientEngineKind.UnityCn => "Unity CN",
        ClientEngineKind.Tuanjie => "Tuanjie",
        ClientEngineKind.Godot => "Godot",
        ClientEngineKind.Console => "Console",
        _ => throw new ArgumentOutOfRangeException(nameof(clientEngine), clientEngine, null)
    };

    public static string GetStarterClientLabel(this ClientEngineKind clientEngine) => clientEngine switch
    {
        ClientEngineKind.Unity => "Unity 2022 LTS",
        ClientEngineKind.UnityCn => "Unity 2022 LTS (China-friendly defaults)",
        ClientEngineKind.Tuanjie => "Tuanjie (Unity-compatible)",
        ClientEngineKind.Godot => "Godot 4.6",
        ClientEngineKind.Console => ".NET console",
        _ => throw new ArgumentOutOfRangeException(nameof(clientEngine), clientEngine, null)
    };

    public static NuGetForUnitySourceKind GetDefaultNuGetForUnitySource(this ClientEngineKind clientEngine) => clientEngine switch
    {
        ClientEngineKind.Unity => NuGetForUnitySourceKind.OpenUpm,
        ClientEngineKind.UnityCn => NuGetForUnitySourceKind.Embedded,
        ClientEngineKind.Tuanjie => NuGetForUnitySourceKind.Embedded,
        ClientEngineKind.Godot => NuGetForUnitySourceKind.Embedded,
        ClientEngineKind.Console => NuGetForUnitySourceKind.Embedded,
        _ => throw new ArgumentOutOfRangeException(nameof(clientEngine), clientEngine, null)
    };
}

internal sealed record ResolvedVersions(
    string Core,
    string Server,
    string Client,
    string Transport,
    string Serializer,
    string Analyzers,
    string? SerializerRuntime,
    string? SerializerRuntimeCore);

internal enum StarterCommandKind
{
    New
}

internal sealed record StarterNewCommandOptions(
    string ProjectName,
    string OutputDir,
    ClientEngineKind? ClientEngine,
    TransportKind? Transport,
    SerializerKind? Serializer,
    NuGetForUnitySourceKind? NuGetForUnitySource,
    bool NoNextSteps);

internal sealed record StarterCliOptions(
    StarterCommandKind Command,
    bool ShowHelp,
    bool ShowVersion,
    StarterNewCommandOptions? NewCommand);

internal sealed record StarterPaths(
    string RootPath,
    string SharedPath,
    string ServerRootPath,
    string ServerAppPath,
    string ClientPath);

internal sealed record StarterTemplateContext(
    string ProjectName,
    string CompanyId,
    ClientEngineKind ClientEngine,
    TransportKind Transport,
    SerializerKind Serializer,
    NuGetForUnitySourceKind NuGetForUnitySource,
    ResolvedVersions Versions,
    StarterPaths Paths)
{
    public string SharedProjectName => Path.GetFileName(Paths.SharedPath);
    public string ServerProjectName => Path.GetFileName(Paths.ServerAppPath);
}

internal sealed record UnityClientArtifacts(
    string Manifest,
    string PackagesConfig,
    string NuGetConfig,
    string Readme,
    string ProjectVersion,
    string EditorBuildSettings,
    string TesterScript,
    string TesterScriptMeta,
    string SceneContent,
    string SceneMeta,
    string AutoOpenSceneEditorScript);

internal static class UnityPackageVersions
{
    public const string Kcp = "2.7.0";
    public const string MicrosoftBclAsyncInterfaces = "10.0.7";
    public const string SystemBuffers = "4.6.1";
    public const string SystemCollectionsImmutable = "10.0.6";
    public const string MicrosoftCodeAnalysisCommon = "5.3.0";
    public const string MicrosoftCodeAnalysisCSharp = "5.3.0";
    public const string SystemReflectionMetadata = "10.0.7";
    public const string SystemTextEncodingCodePages = "10.0.7";
    public const string SystemThreadingTasksExtensionsForRoslyn = "4.5.4";
    public const string SystemMemoryForRoslyn = "4.5.4";
    public const string SystemIoPipelinesForJson = "10.0.6";
    public const string SystemMemoryForJson = "4.6.3";
    public const string SystemMemoryForKcp = "4.5.4";
    public const string SystemThreadingChannels = "10.0.7";
    public const string SystemTextEncodingsWeb = "10.0.7";
    public const string SystemTextJson = "10.0.7";
    public const string SystemThreadingTasksExtensionsForJson = "4.6.3";
    public const string SystemThreadingTasksExtensionsForKcp = "4.5.4";
    public const string SystemRuntimeCompilerServicesUnsafe = "6.1.2";
    public const string SystemIoPipelines = "10.0.6";
}
