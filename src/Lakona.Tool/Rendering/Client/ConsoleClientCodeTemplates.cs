using Lakona.Tool.Domain;

namespace Lakona.Tool.Rendering.Client;

internal static class ConsoleClientCodeTemplates
{
    public static string RenderProgram(LakonaProjectSpec spec)
    {
        return """
        namespace Client;

        internal static class Program
        {
            public static Task<int> Main(string[] args)
            {
                return Task.FromResult(0);
            }
        }
        """;
    }

    public static string RenderConsoleClientSettings()
    {
        return """
        namespace Client.ClientRuntime;

        public sealed record ConsoleClientSettings(string Host, int Port, string Path);
        """;
    }

    public static string RenderRpcClientFactory(LakonaProjectSpec spec)
    {
        return $$"""
        using Lakona.Rpc.Client;
        using Lakona.Rpc.Core;
        {{RenderSerializerUsing(spec.Serializer)}}
        {{RenderTransportUsing(spec.Transport)}}

        namespace Client.ClientRuntime;

        public static class RpcClientFactory
        {
            public static RpcClient Create(ConsoleClientSettings settings)
            {
                return new RpcClient(new RpcClientOptions(
                    {{RenderTransportExpression(spec.Transport)}},
                    {{RenderSerializerExpression(spec.Serializer)}})
                    .UseSecurity(ConfigureTransportSecurity));
            }

            private static string NormalizePath(string path)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return string.Empty;
                }

                return path.StartsWith("/", StringComparison.Ordinal) ? path : "/" + path;
            }

            private static void ConfigureTransportSecurity(TransportSecurityConfig security)
            {
                security.EnableCompression = false;
                security.CompressionThresholdBytes = 1024;
                security.EnableEncryption = false;
                security.EncryptionKeyBase64 = null;
            }
        }
        """;
    }

    public static string RenderLoginChatLoadScenario()
    {
        return """
        using Lakona.Game.LoadTesting;

        namespace Client.LoadScenarios;

        public sealed class LoginChatLoadScenario : ILoadScenario
        {
            public string Name => "login-chat";

            public ValueTask RunUserAsync(LoadUserContext context, CancellationToken cancellationToken)
            {
                return default;
            }
        }
        """;
    }

    public static string RenderLoginChatLoadScenarioOptions()
    {
        return """
        using Client.ClientRuntime;

        namespace Client.LoadScenarios;

        public sealed record LoginChatLoadScenarioOptions(ConsoleClientSettings ClientSettings, int MessageRatePerUser);
        """;
    }

    private static string RenderTransportUsing(TransportKind transport) => transport switch
    {
        TransportKind.Tcp => "using Lakona.Rpc.Transport.Tcp;",
        TransportKind.WebSocket => "using Lakona.Rpc.Transport.WebSocket;",
        TransportKind.Kcp => "using Lakona.Rpc.Transport.Kcp;",
        _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, null)
    };

    private static string RenderSerializerUsing(SerializerKind serializer) => serializer switch
    {
        SerializerKind.Json => "using Lakona.Rpc.Serializer.Json;",
        SerializerKind.MemoryPack => "using Lakona.Rpc.Serializer.MemoryPack;",
        _ => throw new ArgumentOutOfRangeException(nameof(serializer), serializer, null)
    };

    private static string RenderTransportExpression(TransportKind transport) => transport switch
    {
        TransportKind.Tcp => "new TcpTransport(settings.Host, settings.Port)",
        TransportKind.WebSocket => "new WsTransport($\"ws://{settings.Host}:{settings.Port}{NormalizePath(settings.Path)}\")",
        TransportKind.Kcp => "new KcpTransport(settings.Host, settings.Port)",
        _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, null)
    };

    private static string RenderSerializerExpression(SerializerKind serializer) => serializer switch
    {
        SerializerKind.Json => "new JsonRpcSerializer()",
        SerializerKind.MemoryPack => "new MemoryPackRpcSerializer()",
        _ => throw new ArgumentOutOfRangeException(nameof(serializer), serializer, null)
    };
}
