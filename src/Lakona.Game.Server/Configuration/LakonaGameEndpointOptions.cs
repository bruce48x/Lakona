namespace Lakona.Game.Server.Configuration;

public sealed class LakonaGameEndpointOptions
{
    public string Transport { get; init; } = "";
    public string Host { get; init; } = "";
    public int Port { get; init; }
    public string Path { get; init; } = "";
    public string AdvertisedHost { get; init; } = "";

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
