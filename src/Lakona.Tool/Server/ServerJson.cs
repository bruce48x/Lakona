using System.Text.Json;

namespace Lakona.Tool.Server;

internal static class ServerJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

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
