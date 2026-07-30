using System.Text.Json;

namespace Lakona.ProjectSystem.Packaging.Hotfix;

internal static class HotfixJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static HotfixJsonContext Context { get; } = new(Options);
}
