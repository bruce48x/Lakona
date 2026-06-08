internal static class PackageCatalog
{
    public static (PackageArtifact PackageId, string SerializerType) GetSerializerArtifacts(string serializer)
    {
        return serializer switch
        {
            "json" => (new PackageArtifact("Lakona.Rpc.Serializer.Json", "", "Lakona.Rpc.Serializer.Json"), "JsonRpcSerializer"),
            _ => (new PackageArtifact("Lakona.Rpc.Serializer.MemoryPack", "", "Lakona.Rpc.Serializer.MemoryPack"), "MemoryPackRpcSerializer")
        };
    }

    public static (PackageArtifact PackageId, string AcceptorType) GetTransportArtifacts(string transport)
    {
        return transport switch
        {
            "tcp" => (new PackageArtifact("Lakona.Rpc.Transport.Tcp", "", "Lakona.Rpc.Transport.Tcp"), "TcpConnectionAcceptor"),
            "websocket" => (new PackageArtifact("Lakona.Rpc.Transport.WebSocket", "", "Lakona.Rpc.Transport.WebSocket"), "WsConnectionAcceptor"),
            _ => (new PackageArtifact("Lakona.Rpc.Transport.Kcp", "", "Lakona.Rpc.Transport.Kcp"), "KcpConnectionAcceptor")
        };
    }
}

internal static class TemplateText
{
    public static string SanitizeStringLiteral(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    public static string SanitizeCSharpIdentifier(string value)
    {
        var sanitized = new string(value.Select(static c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return "Game";
        }

        return char.IsDigit(sanitized[0]) ? "_" + sanitized : sanitized;
    }

    public static string IndentBlock(string block, int level)
    {
        if (string.IsNullOrWhiteSpace(block))
        {
            return string.Empty;
        }

        var indent = new string(' ', level * 4);
        var lines = block.Replace("\r\n", "\n").Split('\n');
        return string.Join(Environment.NewLine, lines.Select(line => string.IsNullOrWhiteSpace(line) ? string.Empty : indent + line));
    }
}
