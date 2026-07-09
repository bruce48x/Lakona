using System.Text.Json;

namespace Lakona.Game.Server.Health;

public sealed record LakonaHealthHttpResponse(
    int StatusCode,
    string ContentType,
    string Body)
{
    private static readonly JsonSerializerOptions DefaultJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static LakonaHealthHttpResponse Json(
        object value,
        int statusCode = 200,
        JsonSerializerOptions? options = null)
    {
        return new LakonaHealthHttpResponse(
            statusCode,
            "application/json",
            JsonSerializer.Serialize(value, options ?? DefaultJsonOptions));
    }
}
