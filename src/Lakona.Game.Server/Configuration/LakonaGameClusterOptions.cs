namespace Lakona.Game.Server.Configuration;

public sealed class LakonaGameClusterOptions
{
    public string Endpoint { get; init; } = "";

    public string Serializer { get; init; } = "";

    public IReadOnlyList<string> Seeds { get; init; } = [];

    public LakonaClusterDirectoryOptions Directory { get; init; } = new();
}
