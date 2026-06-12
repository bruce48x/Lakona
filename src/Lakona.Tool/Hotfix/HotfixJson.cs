using System.Text.Json;

namespace Lakona.Tool.Hotfix;

internal static class HotfixJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
}
