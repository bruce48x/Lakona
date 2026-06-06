using Microsoft.Extensions.Configuration;

namespace Lakona.Game.Server.Configuration;

public sealed class ServerRpcServerOptions
{
    public string Transport { get; init; } = "websocket";
    public string Host { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 20000;
    public string Path { get; init; } = "";

    public static ServerRpcServerOptions FromConfiguration(
        IConfiguration configuration,
        string sectionName,
        ServerRpcServerOptions defaults)
    {
        var section = configuration.GetSection(sectionName);
        var transport = NormalizeTransport(section["Transport"], defaults.Transport);
        var host = section["Host"];
        var path = section["Path"];

        return new ServerRpcServerOptions
        {
            Transport = transport,
            Host = string.IsNullOrWhiteSpace(host) ? defaults.Host : host,
            Port = ParsePort(section["Port"], defaults.Port),
            Path = string.IsNullOrWhiteSpace(path) ? defaults.Path : path
        };
    }

    private static string NormalizeTransport(string? rawValue, string fallback)
    {
        return string.IsNullOrWhiteSpace(rawValue)
            ? fallback
            : rawValue.Trim().ToLowerInvariant();
    }

    private static int ParsePort(string? rawValue, int fallback)
    {
        return int.TryParse(rawValue, out var port) && port > 0
            ? port
            : fallback;
    }
}
