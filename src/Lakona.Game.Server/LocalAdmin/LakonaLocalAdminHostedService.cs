using System.Net;
using System.Text;
using Lakona.Game.Server.Observability;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.LocalAdmin;

public sealed class LakonaLocalAdminHostedService : BackgroundService
{
    private readonly LakonaObservabilityOptions _options;
    private readonly LakonaLocalAdminRouter _router;
    private readonly ILogger<LakonaLocalAdminHostedService> _logger;
    private readonly LakonaLocalAdminRequestTracker _requestTracker;
    private HttpListener? _listener;

    public LakonaLocalAdminHostedService(
        LakonaObservabilityOptions options,
        LakonaLocalAdminRouter router,
        ILogger<LakonaLocalAdminHostedService> logger)
        : this(options, router, logger, new LakonaLocalAdminRequestTracker())
    {
    }

    internal LakonaLocalAdminHostedService(
        LakonaObservabilityOptions options,
        LakonaLocalAdminRouter router,
        ILogger<LakonaLocalAdminHostedService> logger,
        LakonaLocalAdminRequestTracker requestTracker)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _requestTracker = requestTracker ?? throw new ArgumentNullException(nameof(requestTracker));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var localAdmin = _options.LocalAdmin;
        if (!localAdmin.EffectiveEnabled)
        {
            return;
        }

        var listener = new HttpListener();
        listener.Prefixes.Add(FormatPrefix(localAdmin.Host, localAdmin.Port));
        listener.Start();
        _listener = listener;
        _logger.LogInformation(
            "Lakona local admin endpoint listening on {Host}:{Port}.",
            localAdmin.Host,
            localAdmin.Port);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var context = await listener.GetContextAsync().WaitAsync(stoppingToken).ConfigureAwait(false);
                _ = _requestTracker.Track(() => HandleAndLogAsync(context, stoppingToken));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (HttpListenerException) when (stoppingToken.IsCancellationRequested || !listener.IsListening)
        {
        }
        finally
        {
            listener.Close();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _listener?.Close();
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

    private async Task HandleAndLogAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            await HandleAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Lakona local admin request handling failed.");
        }
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            var request = context.Request;
            var response = await _router.RouteAsync(
                new LakonaLocalAdminRequest(
                    request.HttpMethod,
                    request.Url?.AbsolutePath ?? "",
                    request.InputStream,
                    IsLoopback(request.RemoteEndPoint?.Address),
                    _options.LocalAdmin.RequireLoopback),
                cancellationToken).ConfigureAwait(false);

            await WriteResponseAsync(context.Response, response, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            context.Response.Close();
        }
    }

    private static bool IsLoopback(IPAddress? address)
    {
        return address is not null && IPAddress.IsLoopback(address);
    }

    private static async Task WriteResponseAsync(
        HttpListenerResponse response,
        LakonaLocalAdminResponse localAdminResponse,
        CancellationToken cancellationToken)
    {
        response.StatusCode = localAdminResponse.StatusCode;
        response.ContentType = localAdminResponse.ContentType;
        var bytes = Encoding.UTF8.GetBytes(localAdminResponse.Body);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }
}
