using System.Net;
using System.Net.Http.Json;

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
        return await http.GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> PostAsync(string server, string path, object body, CancellationToken cancellationToken)
    {
        var uri = CreateLoopbackUri(server, path);
        using var response = await http.PostAsJsonAsync(uri, body, HotfixJson.Options, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private static Uri CreateLoopbackUri(string server, string path)
    {
        if (!IsLoopbackServer(server))
        {
            throw new InvalidOperationException("Hotfix admin server URL must be loopback.");
        }

        return new Uri(new Uri(server.TrimEnd('/') + "/"), path.TrimStart('/'));
    }
}
