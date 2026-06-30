using System.Text.Json;

namespace Lakona.Game.Server.LocalAdmin;

public sealed record LakonaLocalAdminResponse(
    int StatusCode,
    string ContentType,
    string Body)
{
    private static readonly JsonSerializerOptions DefaultJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static LakonaLocalAdminResponse Json(
        object value,
        int statusCode = 200,
        JsonSerializerOptions? options = null)
    {
        return new LakonaLocalAdminResponse(
            statusCode,
            "application/json",
            JsonSerializer.Serialize(value, options ?? DefaultJsonOptions));
    }
}
