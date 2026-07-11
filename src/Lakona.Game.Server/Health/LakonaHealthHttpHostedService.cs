using System.Net;
using System.Net.Sockets;
using System.Text;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.InternalHttp;
using Lakona.Game.Server.Observability;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Health;

public sealed class LakonaHealthHttpHostedService : BackgroundService
{
    private readonly LakonaGameRuntimeOptions _options;
    private readonly LakonaObservabilityOptions _observabilityOptions;
    private readonly LakonaHttpRouter _router;
    private readonly ILogger<LakonaHealthHttpHostedService> _logger;
    private readonly LakonaHealthHttpRequestTracker _requestTracker;
    private readonly TaskCompletionSource _listening = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private TcpListener? _listener;

    public LakonaHealthHttpHostedService(
        LakonaGameRuntimeOptions options,
        LakonaObservabilityOptions observabilityOptions,
        LakonaHttpRouter router,
        ILogger<LakonaHealthHttpHostedService> logger)
        : this(options, observabilityOptions, router, logger, new LakonaHealthHttpRequestTracker())
    {
    }

    internal LakonaHealthHttpHostedService(
        LakonaGameRuntimeOptions options,
        LakonaObservabilityOptions observabilityOptions,
        LakonaHttpRouter router,
        ILogger<LakonaHealthHttpHostedService> logger,
        LakonaHealthHttpRequestTracker requestTracker)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _observabilityOptions = observabilityOptions ?? throw new ArgumentNullException(nameof(observabilityOptions));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _requestTracker = requestTracker ?? throw new ArgumentNullException(nameof(requestTracker));
    }

    internal Task Listening => _listening.Task;

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await base.StartAsync(cancellationToken).ConfigureAwait(false);
        await _listening.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var http = _options.Health.Http;
            if (!http.Enabled && !_observabilityOptions.LocalAdmin.EffectiveEnabled)
            {
                _listening.TrySetResult();
                return;
            }

            stoppingToken.ThrowIfCancellationRequested();
            var listener = new TcpListener(ResolveBindAddress(http.Host), http.Port);
            _listener = listener;
            listener.Start();
            _logger.LogInformation(
                "Lakona health endpoint listening on {Host}:{Port}.",
                http.Host,
                http.Port);
            _listening.TrySetResult();

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
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _listening.TrySetCanceled(stoppingToken);
        }
        catch (Exception exception)
        {
            _listening.TrySetException(exception);
            throw;
        }
        finally
        {
            _listener?.Stop();
            if (!_listening.Task.IsCompleted)
            {
                _listening.TrySetCanceled(stoppingToken);
            }
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
            var request = await ReadRequestAsync(stream, cancellationToken).ConfigureAwait(false);
            var remoteAddress = (client.Client.RemoteEndPoint as IPEndPoint)?.Address;

            var response = request is null
                ? LakonaHttpResponse.Json(new { error = "Invalid health request." }, 400)
                : await _router.RouteAsync(
                    new LakonaHttpRequest(
                        request.Value.Method,
                        request.Value.Path,
                        request.Value.Body,
                        IsLoopback(remoteAddress)),
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
        LakonaHttpResponse healthResponse,
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

    private static async Task<(string Method, string Path, Stream Body)?> ReadRequestAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        var length = 0;
        var headerLength = -1;
        while (length < buffer.Length && headerLength < 0)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return null;
            }

            length += read;
            headerLength = FindHeaderEnd(buffer, length);
        }

        if (headerLength < 0)
        {
            return null;
        }

        var headers = Encoding.ASCII.GetString(buffer, 0, headerLength);
        var lineEnd = headers.IndexOf("\r\n", StringComparison.Ordinal);
        var request = ParseRequestLine(lineEnd >= 0 ? headers[..lineEnd] : headers);
        if (request is null)
        {
            return null;
        }

        var contentLength = headers.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Select(static line => line.Split(':', 2))
            .Where(static parts => parts.Length == 2 && string.Equals(parts[0].Trim(), "Content-Length", StringComparison.OrdinalIgnoreCase))
            .Select(static parts => int.TryParse(parts[1].Trim(), out var value) ? value : 0)
            .FirstOrDefault();
        var bodyOffset = headerLength + 4;
        if (contentLength < 0 || contentLength > buffer.Length - bodyOffset)
        {
            return null;
        }

        var bodyRead = length - bodyOffset;
        while (bodyRead < contentLength)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(bodyOffset + bodyRead, contentLength - bodyRead), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return null;
            }

            bodyRead += read;
        }

        return (request.Value.Method, request.Value.Path, new MemoryStream(buffer, bodyOffset, contentLength, writable: false));
    }

    private static int FindHeaderEnd(byte[] buffer, int length)
    {
        for (var index = 0; index <= length - 4; index++)
        {
            if (buffer[index] == '\r' && buffer[index + 1] == '\n'
                && buffer[index + 2] == '\r' && buffer[index + 3] == '\n')
            {
                return index;
            }
        }

        return -1;
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
