namespace Lakona.Game.Server.Configuration;

/// <summary>
/// Configures one client-facing RPC listener under <c>Lakona:Endpoints</c>.
/// </summary>
public sealed class LakonaGameEndpointOptions
{
    /// <summary>
    /// Gets the transport name, such as <c>websocket</c>, <c>kcp</c>, or <c>tcp</c>.
    /// </summary>
    public string Transport { get; init; } = "";

    /// <summary>
    /// Gets the serializer used for business RPC payloads on this endpoint.
    /// </summary>
    public string Serializer { get; init; } = "";

    /// <summary>
    /// Gets the local host address the listener binds to.
    /// </summary>
    public string Host { get; init; } = "";

    /// <summary>
    /// Gets the local port the listener binds to.
    /// </summary>
    public int Port { get; init; }

    /// <summary>
    /// Gets the transport path, required for WebSocket endpoints and empty for KCP endpoints.
    /// </summary>
    public string Path { get; init; } = "";

    /// <summary>
    /// Gets the externally reachable host advertised to clients and other nodes.
    /// </summary>
    public string AdvertisedHost { get; init; } = "";

    /// <summary>
    /// Gets the RPC service names exposed through this endpoint.
    /// </summary>
    public IReadOnlyList<string> RpcServices { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Builds the externally advertised endpoint URI.
    /// </summary>
    /// <returns>The advertised endpoint URI.</returns>
    public string ToAdvertisedEndpoint()
    {
        var normalizedTransport = Transport.ToLowerInvariant();
        var scheme = normalizedTransport switch
        {
            "websocket" => "ws",
            "tcp" => "tcp",
            _ => normalizedTransport
        };
        var host = string.IsNullOrWhiteSpace(AdvertisedHost) ? Host : AdvertisedHost;

        return string.IsNullOrWhiteSpace(Path)
            ? $"{scheme}://{host}:{Port}"
            : $"{scheme}://{host}:{Port}{Path}";
    }

    /// <summary>
    /// Gets the default path for the configured transport.
    /// </summary>
    /// <returns>The default path, or an empty string when the transport does not use paths.</returns>
    public string GetDefaultPath()
    {
        var normalizedTransport = Transport.ToLowerInvariant();
        return normalizedTransport switch
        {
            "websocket" => "/ws",
            _ => ""
        };
    }
}
