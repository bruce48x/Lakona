using Microsoft.Extensions.Configuration;

namespace Lakona.Game.Server.Configuration;

public sealed class LakonaLoggingOptions
{
    public LakonaRpcLoggingOptions Rpc { get; init; } = new();
    public LakonaRpcLoggingOptions ClusterRpc { get; init; } = new();

    internal static LakonaLoggingOptions Bind(IConfiguration section) => new()
    {
        Rpc = LakonaRpcLoggingOptions.Bind(section.GetSection("Rpc")),
        ClusterRpc = LakonaRpcLoggingOptions.Bind(section.GetSection("ClusterRpc"))
    };
}

public sealed class LakonaRpcLoggingOptions
{
    public bool IncludePayload { get; init; }
    public int MaxPayloadCharacters { get; init; } = 4096;
    public IReadOnlyList<string> RedactedMembers { get; init; } =
        ["Password", "Token", "AccessToken", "RefreshToken", "ResumeTicket", "Secret", "Payload"];

    internal static LakonaRpcLoggingOptions Bind(IConfigurationSection section)
    {
        var limit = LakonaConfigurationReader.ReadInt(section, "MaxPayloadCharacters", 4096);
        if (limit < 64 || limit > 65536)
            throw new InvalidOperationException($"{section.Path}:MaxPayloadCharacters must be between 64 and 65536.");
        var defaults = new LakonaRpcLoggingOptions();
        return new()
        {
            IncludePayload = LakonaConfigurationReader.ReadBool(section, "IncludePayload", false),
            MaxPayloadCharacters = limit,
            RedactedMembers = defaults.RedactedMembers.Concat(
                LakonaConfigurationReader.ReadStringArray(section.GetSection("RedactedMembers"))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }
}
