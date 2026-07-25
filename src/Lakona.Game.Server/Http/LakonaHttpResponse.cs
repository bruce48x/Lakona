using System.Text;
using System.Text.Json;

namespace Lakona.Game.Server.Http;

/// <summary>
/// Materialized response returned by a Hotfix HTTP handler.
/// </summary>
public sealed record LakonaHttpResponse(
    int StatusCode,
    string ContentType,
    ReadOnlyMemory<byte> Body,
    IReadOnlyDictionary<string, string[]> Headers)
{
    public static LakonaHttpResponse Text(
        string body,
        int statusCode = 200,
        string contentType = "text/plain; charset=utf-8")
    {
        return new LakonaHttpResponse(
            statusCode,
            contentType,
            Encoding.UTF8.GetBytes(body),
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase));
    }

    public static LakonaHttpResponse Json<T>(
        T value,
        int statusCode = 200,
        JsonSerializerOptions? options = null)
    {
        return new LakonaHttpResponse(
            statusCode,
            "application/json; charset=utf-8",
            JsonSerializer.SerializeToUtf8Bytes(value, options),
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase));
    }
}
