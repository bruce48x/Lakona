using Lakona.Game.Server.Configuration;

namespace Lakona.Game.Server.Guardrails.Rules;

internal static class EndpointRule
{
    private static readonly HashSet<string> KnownTransports = new(StringComparer.OrdinalIgnoreCase)
    {
        "kcp",
        "tcp",
        "websocket"
    };

    private static readonly HashSet<string> KnownSerializers = new(StringComparer.OrdinalIgnoreCase)
    {
        "json",
        "memorypack"
    };

    internal static IEnumerable<LakonaGameDiagnostic> Validate(LakonaGameRuntimeOptions runtime)
    {
        var transports = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var bindAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < runtime.Endpoints.Count; index++)
        {
            var endpoint = runtime.Endpoints[index];
            var path = $"Lakona:Endpoints:{index}";
            var rpcServices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var transport = endpoint.Transport;
            if (string.IsNullOrWhiteSpace(transport))
            {
                yield return Error("LAKONA10020", "Endpoint transport is required.", $"{path}:Transport");
            }
            else
            {
                if (!KnownTransports.Contains(transport))
                {
                    yield return Error("LAKONA10020", $"Endpoint transport '{transport}' is unknown.", $"{path}:Transport", "Use kcp, tcp, or websocket.");
                }

                if (string.Equals(transport, "websocket", StringComparison.OrdinalIgnoreCase)
                    && string.IsNullOrWhiteSpace(endpoint.Path))
                {
                    yield return Error("LAKONA10023", "WebSocket endpoint requires Path.", $"{path}:Path", "Set Path to a path such as /ws.");
                }

                if (string.Equals(transport, "kcp", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(endpoint.Path))
                {
                    yield return Error("LAKONA10025", "KCP endpoint must not set Path.", $"{path}:Path", "Remove Path from the KCP endpoint.");
                }

                if (!transports.Add(transport))
                {
                    yield return Error("LAKONA10024", $"Endpoint transport '{transport}' is configured more than once.", $"{path}:Transport");
                }
            }

            var serializer = endpoint.Serializer;
            if (string.IsNullOrWhiteSpace(serializer))
            {
                yield return Error("LAKONA10028", "Endpoint serializer is required.", $"{path}:Serializer");
            }
            else if (!KnownSerializers.Contains(serializer))
            {
                yield return Error(
                    "LAKONA10028",
                    $"Endpoint serializer '{serializer}' is unknown.",
                    $"{path}:Serializer",
                    "Use json or memorypack.");
            }

            if (string.IsNullOrWhiteSpace(endpoint.Host))
            {
                yield return Error("LAKONA10021", "Endpoint host is required.", $"{path}:Host");
            }

            if (endpoint.Port <= 0 || endpoint.Port > 65535)
            {
                yield return Error("LAKONA10022", "Endpoint port must be between 1 and 65535.", $"{path}:Port");
            }

            if (endpoint.ConnectionLimits.MaxActiveConnections <= 0)
            {
                yield return Error(
                    "LAKONA10029",
                    "Endpoint MaxActiveConnections must be positive.",
                    $"{path}:ConnectionLimits:MaxActiveConnections");
            }

            if (endpoint.ConnectionLimits.MaxPendingHandshakes <= 0
                || endpoint.ConnectionLimits.MaxPendingHandshakes > endpoint.ConnectionLimits.MaxActiveConnections)
            {
                yield return Error(
                    "LAKONA10029",
                    "Endpoint MaxPendingHandshakes must be positive and cannot exceed MaxActiveConnections.",
                    $"{path}:ConnectionLimits:MaxPendingHandshakes");
            }

            if (endpoint.ConnectionLimits.HandshakeTimeout <= TimeSpan.Zero)
            {
                yield return Error(
                    "LAKONA10029",
                    "Endpoint HandshakeTimeout must be positive.",
                    $"{path}:ConnectionLimits:HandshakeTimeout");
            }

            var bind = $"{endpoint.Host}:{endpoint.Port}";
            if (!string.IsNullOrWhiteSpace(endpoint.Host)
                && endpoint.Port > 0
                && !bindAddresses.Add(bind))
            {
                yield return Error("LAKONA10026", $"Endpoint bind address '{bind}' is configured more than once.", $"{path}:Port");
            }

            foreach (var rpcService in endpoint.RpcServices)
            {
                if (!rpcServices.Add(rpcService))
                {
                    yield return Error(
                        "LAKONA10027",
                        $"Endpoint RPC service '{rpcService}' is configured more than once.",
                        $"{path}:Transport");
                }
            }
        }
    }

    private static LakonaGameDiagnostic Error(string code, string message, string? path, string? repair = null)
    {
        var fullMessage = string.IsNullOrWhiteSpace(path) ? message : $"{path}: {message}";
        return new LakonaGameDiagnostic(
            code,
            LakonaGameDiagnosticSeverity.Error,
            fullMessage,
            repair);
    }
}
