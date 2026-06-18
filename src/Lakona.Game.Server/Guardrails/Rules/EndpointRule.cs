namespace Lakona.Game.Server.Guardrails.Rules;

public sealed class EndpointRule : ILakonaGameValidationRule
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

    public IEnumerable<LakonaGameDiagnostic> Validate(LakonaGameResolvedRuntime runtime)
    {
        var transports = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var bindAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var endpoint in runtime.Endpoints)
        {
            var rpcServices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var transport = endpoint.Transport.Value;
            if (string.IsNullOrWhiteSpace(transport))
            {
                yield return Error("ULINK020", "Endpoint transport is required.", endpoint.Transport.Path);
            }
            else
            {
                if (!KnownTransports.Contains(transport))
                {
                    yield return Error("ULINK020", $"Endpoint transport '{transport}' is unknown.", endpoint.Transport.Path, "Use kcp, tcp, or websocket.");
                }

                if (string.Equals(transport, "websocket", StringComparison.OrdinalIgnoreCase)
                    && string.IsNullOrWhiteSpace(endpoint.Path.Value))
                {
                    yield return Error("ULINK023", "WebSocket endpoint requires Path.", endpoint.Path.Path, "Set Path to a path such as /ws.");
                }

                if (string.Equals(transport, "kcp", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(endpoint.Path.Value))
                {
                    yield return Error("ULINK025", "KCP endpoint must not set Path.", endpoint.Path.Path, "Remove Path from the KCP endpoint.");
                }

                if (!transports.Add(transport))
                {
                    yield return Error("ULINK024", $"Endpoint transport '{transport}' is configured more than once.", endpoint.Transport.Path);
                }
            }

            var serializer = endpoint.Serializer.Value;
            if (string.IsNullOrWhiteSpace(serializer))
            {
                yield return Error("ULINK028", "Endpoint serializer is required.", endpoint.Serializer.Path);
            }
            else if (!KnownSerializers.Contains(serializer))
            {
                yield return Error(
                    "ULINK028",
                    $"Endpoint serializer '{serializer}' is unknown.",
                    endpoint.Serializer.Path,
                    "Use json or memorypack.");
            }

            if (string.IsNullOrWhiteSpace(endpoint.Host.Value))
            {
                yield return Error("ULINK021", "Endpoint host is required.", endpoint.Host.Path);
            }

            if (endpoint.Port.Value <= 0 || endpoint.Port.Value > 65535)
            {
                yield return Error("ULINK022", "Endpoint port must be between 1 and 65535.", endpoint.Port.Path);
            }

            var bind = $"{endpoint.Host.Value}:{endpoint.Port.Value}";
            if (!string.IsNullOrWhiteSpace(endpoint.Host.Value)
                && endpoint.Port.Value > 0
                && !bindAddresses.Add(bind))
            {
                yield return Error("ULINK026", $"Endpoint bind address '{bind}' is configured more than once.", endpoint.Port.Path);
            }

            foreach (var rpcService in endpoint.RpcServices)
            {
                if (!rpcServices.Add(rpcService))
                {
                    yield return Error(
                        "ULINK027",
                        $"Endpoint RPC service '{rpcService}' is configured more than once.",
                        endpoint.Transport.Path);
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
