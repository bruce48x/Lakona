using System.Net;
using System.Net.Sockets;
using System.Text;
using Lakona.Game.Server.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Health;

public sealed class LakonaHealthHttpHostedService : BackgroundService
{
    private readonly LakonaGameRuntimeOptions _options;
    private readonly LakonaHealthHttpRouter _router;
    private readonly ILogger<LakonaHealthHttpHostedService> _logger;
    private readonly LakonaHealthHttpRequestTracker _requestTracker;
    private TcpListener? _listener;

    public LakonaHealthHttpHostedService(
        LakonaGameRuntimeOptions options,
        LakonaHealthHttpRouter router,
        ILogger<LakonaHealthHttpHostedService> logger)
        : this(options, router, logger, new LakonaHealthHttpRequestTracker())
    {
    }

    internal LakonaHealthHttpHostedService(
        LakonaGameRuntimeOptions options,
        LakonaHealthHttpRouter router,
        ILogger<LakonaHealthHttpHostedService> logger,
        LakonaHealthHttpRequestTracker requestTracker)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _requestTracker = requestTracker ?? throw new ArgumentNullException(nameof(requestTracker));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var http = _options.Health.Http;
        if (!http.Enabled)
        {
            return;
        }

        var listener = new TcpListener(ResolveBindAddress(http.Host), http.Port);
        listener.Start();
        _listener = listener;
        _logger.LogInformation(
            "Lakona health endpoint listening on {Host}:{Port}.",
            http.Host,
            http.Port);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stoppingToken).ConfigureAwait(false);
                _ = _requestTracker.Track(() => HandleAndLogAsync(client, stoppingToken));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (SocketException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            listener.Stop();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _listener?.Stop();
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        await _requestTracker.DrainAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static string FormatPrefixForTesting(string host, int port)
    {
        return FormatPrefix(host, port);
    }

    private static string FormatPrefix(string host, int port)
    {
        var formattedHost = host.Contains(':', StringComparison.Ordinal) && !host.StartsWith("[", StringComparison.Ordinal)
            ? $"[{host}]"
            : host;
        return $"http://{formattedHost}:{port}/";
    }

    private static IPAddress ResolveBindAddress(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return IPAddress.Loopback;
        }

        if (host is "*" or "+")
        {
            return IPAddress.Any;
        }

        if (IPAddress.TryParse(host, out var address))
        {
            return address;
        }

        return IPAddress.Loopback;
    }

    private async Task HandleAndLogAsync(TcpClient client, CancellationToken cancellationToken)
    {
        try
        {
            await HandleAsync(client, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Lakona health request handling failed.");
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        await using (var stream = client.GetStream())
        {
            var requestLine = await ReadRequestLineAsync(stream, cancellationToken).ConfigureAwait(false);
            var request = ParseRequestLine(requestLine);
            var remoteAddress = (client.Client.RemoteEndPoint as IPEndPoint)?.Address;

            var response = request is null
                ? LakonaHealthHttpResponse.Json(new { error = "Invalid health request." }, 400)
                : await _router.RouteAsync(
                    new LakonaHealthHttpRequest(
                        request.Value.Method,
                        request.Value.Path,
                        IsLoopback(remoteAddress),
                        _options.Health.Http.RequireLoopback),
                    cancellationToken).ConfigureAwait(false);

            await WriteResponseAsync(stream, response, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsLoopback(IPAddress? address)
    {
        return address is not null && IPAddress.IsLoopback(address);
    }

    private static async Task WriteResponseAsync(
        NetworkStream stream,
        LakonaHealthHttpResponse healthResponse,
        CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(healthResponse.Body);
        var statusText = healthResponse.StatusCode switch
        {
            200 => "OK",
            400 => "Bad Request",
            403 => "Forbidden",
            404 => "Not Found",
            503 => "Service Unavailable",
            _ => "OK"
        };
        var header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {healthResponse.StatusCode} {statusText}\r\n" +
            $"Content-Type: {healthResponse.ContentType}; charset=utf-8\r\n" +
            $"Content-Length: {bytes.Length}\r\n" +
            "Connection: close\r\n" +
            "\r\n");
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> ReadRequestLineAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[2048];
        var length = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        if (length == 0)
        {
            return "";
        }

        var text = Encoding.ASCII.GetString(buffer, 0, length);
        var lineEnd = text.IndexOf("\r\n", StringComparison.Ordinal);
        return lineEnd >= 0 ? text[..lineEnd] : text;
    }

    private static (string Method, string Path)? ParseRequestLine(string requestLine)
    {
        var parts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return null;
        }

        var path = parts[1];
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri))
        {
            path = uri.AbsolutePath;
        }

        var queryIndex = path.IndexOf('?', StringComparison.Ordinal);
        if (queryIndex >= 0)
        {
            path = path[..queryIndex];
        }

        return (parts[0], path);
    }
}
