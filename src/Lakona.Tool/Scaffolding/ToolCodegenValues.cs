using Lakona.Tool.RpcStarter;

internal sealed record ToolCodegenValues(
    string TransportUsing,
    string SerializerUsing,
    string ServerAcceptorFactory,
    string UnityChatTransportConstruction,
    string SerializerConstruction,
    string TransportLabel,
    string DefaultPath)
{
    public static ToolCodegenValues Create(TransportKind transport, SerializerKind serializer)
    {
        return new ToolCodegenValues(
            GetTransportUsing(transport),
            GetSerializerUsing(serializer),
            GetServerAcceptorFactory(transport),
            GetUnityChatTransportConstruction(transport),
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

    private static string GetServerAcceptorFactory(TransportKind transport) => transport switch
    {
        TransportKind.Tcp => "TcpConnectionAcceptor.CreateAsync(builder.ResolvePort(20000), builder.Limits.MaxPendingAcceptedConnections, ct)",
        TransportKind.WebSocket => "WsConnectionAcceptor.CreateAsync(builder.ResolvePort(20000), \"/ws\", builder.Limits.MaxPendingAcceptedConnections, ct)",
        TransportKind.Kcp => "KcpConnectionAcceptor.CreateAsync(builder.ResolvePort(20000), builder.Limits.MaxPendingAcceptedConnections, ct)",
        _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, null)
    };

    private static string GetUnityChatTransportConstruction(TransportKind transport) => transport switch
    {
        TransportKind.Tcp => "new TcpTransport(_serverHost, _serverPort)",
        TransportKind.WebSocket => "new WsTransport($\"ws://{_serverHost}:{_serverPort}{NormalizePath(_serverPath)}\")",
        TransportKind.Kcp => "new KcpTransport(_serverHost, _serverPort)",
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
