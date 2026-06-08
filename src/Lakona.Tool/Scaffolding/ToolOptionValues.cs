using Lakona.Tool.RpcStarter;

internal static class ToolOptionValues
{
    public static ClientEngineKind ParseClientEngine(string value) => value switch
    {
        "unity" => ClientEngineKind.Unity,
        "unity-cn" => ClientEngineKind.UnityCn,
        "tuanjie" => ClientEngineKind.Tuanjie,
        "godot" => ClientEngineKind.Godot,
        _ => throw new InvalidOperationException($"Unsupported --client-engine value after validation: {value}")
    };

    public static TransportKind ParseTransport(string value) => value switch
    {
        "tcp" => TransportKind.Tcp,
        "websocket" => TransportKind.WebSocket,
        "kcp" => TransportKind.Kcp,
        _ => throw new InvalidOperationException($"Unsupported --transport value after validation: {value}")
    };

    public static SerializerKind ParseSerializer(string value) => value switch
    {
        "json" => SerializerKind.Json,
        "memorypack" => SerializerKind.MemoryPack,
        _ => throw new InvalidOperationException($"Unsupported --serializer value after validation: {value}")
    };

    public static NuGetForUnitySourceKind ParseNuGetForUnitySource(string value) => value switch
    {
        "embedded" => NuGetForUnitySourceKind.Embedded,
        "openupm" => NuGetForUnitySourceKind.OpenUpm,
        _ => throw new InvalidOperationException($"Unsupported --nugetforunity-source value after validation: {value}")
    };
}
