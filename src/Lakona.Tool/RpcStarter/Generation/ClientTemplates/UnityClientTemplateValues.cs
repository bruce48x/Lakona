namespace Lakona.Tool.RpcStarter;

internal sealed class UnityClientTemplateValues
{
    private UnityClientTemplateValues(
        string transportUsing,
        string serializerUsing,
        string endpointFactory,
        string transportConstruction,
        string serializerConstruction,
        string transportLabel,
        string defaultPath)
    {
        TransportUsing = transportUsing;
        SerializerUsing = serializerUsing;
        EndpointFactory = endpointFactory;
        TransportConstruction = transportConstruction;
        SerializerConstruction = serializerConstruction;
        TransportLabel = transportLabel;
        DefaultPath = defaultPath;
    }

    public string TransportUsing { get; }

    public string SerializerUsing { get; }

    public string EndpointFactory { get; }

    public string TransportConstruction { get; }

    public string SerializerConstruction { get; }

    public string TransportLabel { get; }

    public string DefaultPath { get; }

    public static UnityClientTemplateValues Create(TransportKind transport, SerializerKind serializer)
    {
        return new UnityClientTemplateValues(
            GetTransportUsing(transport),
            GetSerializerUsing(serializer),
            GetEndpointFactory(transport),
            GetTransportConstruction(transport),
            GetSerializerConstruction(serializer),
            GetTransportLabel(transport),
            transport == TransportKind.WebSocket ? "/ws" : string.Empty);
    }

    private static string GetTransportUsing(TransportKind transport) => transport switch
    {
        TransportKind.Tcp => "using Lakona.Rpc.Transport.Tcp;",
        TransportKind.WebSocket => "using Lakona.Rpc.Transport.WebSocket;",
        TransportKind.Kcp => "using Lakona.Rpc.Transport.Kcp;",
        _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, null)
    };

    private static string GetSerializerUsing(SerializerKind serializer) => serializer switch
    {
        SerializerKind.Json => "using Lakona.Rpc.Serializer.Json;",
        SerializerKind.MemoryPack => "using Lakona.Rpc.Serializer.MemoryPack;",
        _ => throw new ArgumentOutOfRangeException(nameof(serializer), serializer, null)
    };

    private static string GetEndpointFactory(TransportKind transport) => transport switch
    {
        TransportKind.Tcp => "        public static RpcEndpointSettings CreateDefault() => new RpcEndpointSettings { Host = \"127.0.0.1\", Port = 20000, Path = string.Empty };",
        TransportKind.WebSocket => "        public static RpcEndpointSettings CreateDefault() => new RpcEndpointSettings { Host = \"127.0.0.1\", Port = 20000, Path = \"/ws\" };",
        TransportKind.Kcp => "        public static RpcEndpointSettings CreateDefault() => new RpcEndpointSettings { Host = \"127.0.0.1\", Port = 20000, Path = string.Empty };",
        _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, null)
    };

    private static string GetTransportConstruction(TransportKind transport) => transport switch
    {
        TransportKind.Tcp => "new TcpTransport(_endpoint.Host, _endpoint.Port)",
        TransportKind.WebSocket => "new WsTransport($\"ws://{_endpoint.Host}:{_endpoint.Port}{NormalizePath(_endpoint.Path)}\")",
        TransportKind.Kcp => "new KcpTransport(_endpoint.Host, _endpoint.Port)",
        _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, null)
    };

    private static string GetSerializerConstruction(SerializerKind serializer) => serializer switch
    {
        SerializerKind.Json => "new JsonRpcSerializer()",
        SerializerKind.MemoryPack => "new MemoryPackRpcSerializer()",
        _ => throw new ArgumentOutOfRangeException(nameof(serializer), serializer, null)
    };

    private static string GetTransportLabel(TransportKind transport) => transport switch
    {
        TransportKind.Tcp => "TCP",
        TransportKind.WebSocket => "WebSocket",
        TransportKind.Kcp => "KCP",
        _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, null)
    };
}
