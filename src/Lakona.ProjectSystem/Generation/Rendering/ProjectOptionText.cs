using Lakona.ProjectSystem.Generation.Domain;

namespace Lakona.ProjectSystem.Generation.Rendering;

internal static class ProjectOptionText
{
    public static string ToCliValue(ClientEngine value) => value switch
    {
        ClientEngine.Unity => "unity",
        ClientEngine.Tuanjie => "tuanjie",
        ClientEngine.Godot => "godot",
        ClientEngine.Console => "console",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    public static string ToCliValue(ClientEngineVersion value) => value switch
    {
        ClientEngineVersion.Unity2022 => "2022",
        ClientEngineVersion.Unity60 => "6.0",
        ClientEngineVersion.Unity63 => "6.3",
        ClientEngineVersion.Tuanjie167 => "1.6.7",
        ClientEngineVersion.Godot46 => "4.6",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    public static string ToCliValue(TransportKind value) => value switch
    {
        TransportKind.Tcp => "tcp",
        TransportKind.WebSocket => "websocket",
        TransportKind.Kcp => "kcp",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    public static string ToCliValue(SerializerKind value) => value switch
    {
        SerializerKind.Json => "json",
        SerializerKind.MemoryPack => "memorypack",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    public static string ToCliValue(NuGetForUnitySource value) => value switch
    {
        NuGetForUnitySource.Embedded => "embedded",
        NuGetForUnitySource.OpenUpm => "openupm",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    public static string ToCliValue(DeploymentProfile value) => value switch
    {
        DeploymentProfile.None => "none",
        DeploymentProfile.Compose => "compose",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    public static string ToCliValue(MembershipProviderKind value) => value switch
    {
        MembershipProviderKind.Memory => "memory",
        MembershipProviderKind.Postgres => "postgres",
        MembershipProviderKind.Redis => "redis",
        MembershipProviderKind.MySql => "mysql",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };
}
