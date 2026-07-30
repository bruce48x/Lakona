using System.Text.Json;

namespace Lakona.ProjectSystem.Packaging.Server;

internal static class ServerJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static ServerJsonContext Context { get; } = new(Options);

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };
        options.Converters.Add(new UtcDateTimeOffsetJsonConverter());
        return options;
    }
}
