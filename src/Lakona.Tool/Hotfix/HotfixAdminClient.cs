using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Lakona.Tool.Hotfix;

internal sealed class HotfixAdminClient
{
    private readonly HttpClient http;

    public HotfixAdminClient(HttpClient? http = null)
    {
        this.http = http ?? new HttpClient();
    }

    public static bool IsLoopbackServer(string server)
    {
        return Uri.TryCreate(server, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address));
    }

    public async Task<string> GetAsync(string server, string path, CancellationToken cancellationToken)
    {
        var uri = CreateLoopbackUri(server, path);
        using var response = await http.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        return await ReadSuccessBodyAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> PostAsync(string server, string path, object body, CancellationToken cancellationToken)
    {
        var uri = CreateLoopbackUri(server, path);
        using var response = await http.PostAsJsonAsync(uri, body, cancellationToken).ConfigureAwait(false);
        return await ReadSuccessBodyAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private static Uri CreateLoopbackUri(string server, string path)
    {
        if (!IsLoopbackServer(server))
        {
            throw new InvalidOperationException("Hotfix admin server URL must be loopback.");
        }

        return new Uri(new Uri(server.TrimEnd('/') + "/"), path.TrimStart('/'));
    }

    private static async Task<string> ReadSuccessBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            return body;
        }

        throw new HotfixAdminRequestException(
            $"Hotfix admin request failed with HTTP {(int)response.StatusCode} ({response.StatusCode}): {FormatFailure(body)}");
    }

    private static string FormatFailure(string body)
    {
        // Read the wire document without coupling the CLI to the server runtime.
        // Older servers and non-JSON proxy failures retain their original body.
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("diagnostic", out var diagnostic) ||
                diagnostic.ValueKind != JsonValueKind.Object) return body;
            string? Read(string name) => diagnostic.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() : null;
            if (Read("code") is not { } code || Read("message") is not { } message ||
                Read("stage") is not { } stage || Read("remediation") is not { } remediation ||
                Read("correlationId") is not { } correlation) return body;

            var lines = new List<string>
            {
                $"[{code}] {message}",
                $"Stage: {stage}; candidate: {Read("candidateVersion") ?? "unknown"}.",
                $"Loaded version: {Read("loadedVersion") ?? "none"}.",
                $"Next step: {remediation}",
                $"Correlation ID: {correlation}"
            };
            if (diagnostic.TryGetProperty("diagnostics", out var details) && details.ValueKind == JsonValueKind.Array)
                foreach (var detail in details.EnumerateArray())
                    if (detail.ValueKind == JsonValueKind.String && detail.GetString() is { } text && text != message)
                        lines.Add(text);
            return string.Join(Environment.NewLine, lines);
        }
        catch (JsonException)
        {
            return body;
        }
    }
}
