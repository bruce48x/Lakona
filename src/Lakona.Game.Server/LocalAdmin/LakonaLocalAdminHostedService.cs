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
    private HttpListener? _listener;

    public LakonaLocalAdminHostedService(
        LakonaObservabilityOptions options,
        LakonaLocalAdminRouter router,
        ILogger<LakonaLocalAdminHostedService> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var localAdmin = _options.LocalAdmin;
        if (!localAdmin.EffectiveEnabled)
        {
            return;
        }

        var listener = new HttpListener();
        listener.Prefixes.Add($"http://{localAdmin.Host}:{localAdmin.Port}/");
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
                _ = Task.Run(() => HandleAsync(context, stoppingToken), CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (HttpListenerException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            listener.Close();
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _listener?.Close();
        return base.StopAsync(cancellationToken);
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            var request = context.Request;
            var body = await ReadBodyAsync(request, cancellationToken).ConfigureAwait(false);
            var response = await _router.RouteAsync(
                new LakonaLocalAdminRequest(
                    request.HttpMethod,
                    request.Url?.AbsolutePath ?? "",
                    body,
                    IsLoopback(request.RemoteEndPoint?.Address)),
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

    private static async Task<string> ReadBodyAsync(
        HttpListenerRequest request,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(
            request.InputStream,
            request.ContentEncoding,
            detectEncodingFromByteOrderMarks: true,
            leaveOpen: true);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
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
