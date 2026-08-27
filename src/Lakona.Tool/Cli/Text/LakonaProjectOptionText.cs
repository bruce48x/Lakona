using Lakona.ProjectSystem;

namespace Lakona.Tool.Cli.Text;

internal static class LakonaProjectOptionText
{
    public static string ToCliValue(LakonaClientEngine value) => value switch
    {
        LakonaClientEngine.Unity => "unity",
        LakonaClientEngine.Tuanjie => "tuanjie",
        LakonaClientEngine.Godot => "godot",
        LakonaClientEngine.Console => "console",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    public static string ToCliValue(LakonaClientEngineVersion value) => value switch
    {
        LakonaClientEngineVersion.Unity2022 => "2022",
        LakonaClientEngineVersion.Unity60 => "6.0",
        LakonaClientEngineVersion.Unity63 => "6.3",
        LakonaClientEngineVersion.Tuanjie167 => "1.6.7",
        LakonaClientEngineVersion.Godot46 => "4.6",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    public static string ToCliValue(LakonaTransport value) => value switch
    {
        LakonaTransport.Tcp => "tcp",
        LakonaTransport.WebSocket => "websocket",
        LakonaTransport.Kcp => "kcp",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    public static string ToCliValue(LakonaSerializer value) => value switch
    {
        LakonaSerializer.Json => "json",
        LakonaSerializer.MemoryPack => "memorypack",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    public static string ToCliValue(LakonaMembershipProvider value) => value switch
    {
        LakonaMembershipProvider.Memory => "memory",
        LakonaMembershipProvider.Postgres => "postgres",
        LakonaMembershipProvider.Redis => "redis",
        LakonaMembershipProvider.MySql => "mysql",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };
}
