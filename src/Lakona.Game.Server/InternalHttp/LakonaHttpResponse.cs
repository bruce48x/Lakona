using System.Text.Json;

namespace Lakona.Game.Server.InternalHttp;

public sealed record LakonaHttpResponse(int StatusCode, string ContentType, string Body)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static LakonaHttpResponse Json(object value, int statusCode = 200, JsonSerializerOptions? options = null)
    {
        return new LakonaHttpResponse(statusCode, "application/json", JsonSerializer.Serialize(value, options ?? JsonOptions));
    }
}
