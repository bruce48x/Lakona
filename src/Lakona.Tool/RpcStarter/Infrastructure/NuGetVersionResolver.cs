namespace Lakona.Tool.RpcStarter;

internal static class NuGetVersionResolver
{
    public static ResolvedVersions ResolveVersions(TransportKind transport, SerializerKind serializer)
    {
        return new ResolvedVersions(
            StarterReleaseVersions.Core,
            StarterReleaseVersions.Server,
            StarterReleaseVersions.Client,
            GetTransportVersion(transport),
            GetSerializerVersion(serializer),
            StarterReleaseVersions.Analyzers,
            serializer is SerializerKind.MemoryPack ? StarterReleaseVersions.MemoryPackRuntime : null,
            serializer is SerializerKind.MemoryPack ? StarterReleaseVersions.MemoryPackRuntimeCore : null);
    }

    public static string GetTransportPackage(TransportKind transport) => transport switch
    {
        TransportKind.Tcp => "Lakona.Rpc.Transport.Tcp",
        TransportKind.WebSocket => "Lakona.Rpc.Transport.WebSocket",
        TransportKind.Kcp => "Lakona.Rpc.Transport.Kcp",
        _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, null)
    };

    public static string GetSerializerPackage(SerializerKind serializer) => serializer switch
    {
        SerializerKind.Json => "Lakona.Rpc.Serializer.Json",
        SerializerKind.MemoryPack => "Lakona.Rpc.Serializer.MemoryPack",
        _ => throw new ArgumentOutOfRangeException(nameof(serializer), serializer, null)
    };

    private static string GetTransportVersion(TransportKind transport) => transport switch
    {
        TransportKind.Tcp => StarterReleaseVersions.TransportTcp,
        TransportKind.WebSocket => StarterReleaseVersions.TransportWebSocket,
        TransportKind.Kcp => StarterReleaseVersions.TransportKcp,
        _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, null)
    };

    private static string GetSerializerVersion(SerializerKind serializer) => serializer switch
    {
        SerializerKind.Json => StarterReleaseVersions.SerializerJson,
        SerializerKind.MemoryPack => StarterReleaseVersions.SerializerMemoryPack,
        _ => throw new ArgumentOutOfRangeException(nameof(serializer), serializer, null)
    };
}

internal static class StarterReleaseVersions
{
    public static string Core => ToolPackageVersions.LakonaRpcCore;
    public static string Server => ToolPackageVersions.LakonaRpcServer;
    public static string Client => ToolPackageVersions.LakonaRpcClient;
    public static string TransportTcp => ToolPackageVersions.LakonaRpcTransportTcp;
    public static string TransportWebSocket => ToolPackageVersions.LakonaRpcTransportWebSocket;
    public static string TransportKcp => ToolPackageVersions.LakonaRpcTransportKcp;
    public static string SerializerJson => ToolPackageVersions.LakonaRpcSerializerJson;
    public static string SerializerMemoryPack => ToolPackageVersions.LakonaRpcSerializerMemoryPack;
    public static string Analyzers => ToolPackageVersions.LakonaRpcAnalyzers;
    public static string MemoryPackRuntime => ToolPackageVersions.MemoryPackRuntime;
    public static string MemoryPackRuntimeCore => ToolPackageVersions.MemoryPackRuntimeCore;
}
