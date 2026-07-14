namespace Lakona.ProjectSystem;

public enum LakonaClientEngine
{
    Unity,
    Tuanjie,
    Godot,
    Console
}

public enum LakonaClientEngineVersion
{
    Unity2022,
    Unity60,
    Unity63,
    Tuanjie167,
    Godot46
}

public enum LakonaTransport
{
    Tcp,
    WebSocket,
    Kcp
}

public enum LakonaSerializer
{
    Json,
    MemoryPack
}

public enum LakonaPersistence
{
    None,
    MySql,
    Postgres
}

public enum LakonaNuGetForUnitySource
{
    Embedded,
    OpenUpm
}

public enum LakonaDeploymentProfile
{
    None,
    Compose
}

public sealed record LakonaProjectCreationRequest(
    string? ProjectName,
    string? OutputPath,
    LakonaClientEngine ClientEngine = LakonaClientEngine.Unity,
    LakonaClientEngineVersion? ClientEngineVersion = null,
    LakonaTransport Transport = LakonaTransport.Kcp,
    LakonaSerializer Serializer = LakonaSerializer.MemoryPack,
    LakonaPersistence Persistence = LakonaPersistence.None,
    LakonaNuGetForUnitySource NuGetForUnitySource = LakonaNuGetForUnitySource.Embedded,
    LakonaDeploymentProfile DeploymentProfile = LakonaDeploymentProfile.None);
