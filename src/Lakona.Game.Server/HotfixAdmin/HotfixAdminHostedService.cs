using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.HotfixAdmin;

public sealed class HotfixAdminHostedService : BackgroundService
{
    private readonly HotfixAdminOptions _options;
    private readonly HotfixAdminController _controller;
    private readonly ILogger<HotfixAdminHostedService> _logger;
    private HttpListener? _listener;

    public HotfixAdminHostedService(
        HotfixAdminOptions options,
        HotfixAdminController controller,
        ILogger<HotfixAdminHostedService> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        _options.Validate();
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://{_options.Host}:{_options.Port}/");
        listener.Start();
        _listener = listener;
        _logger.LogInformation("Lakona hotfix admin endpoint listening on {Host}:{Port}.", _options.Host, _options.Port);

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
            if (!IsLoopback(context.Request.RemoteEndPoint?.Address))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await WriteJsonAsync(context.Response, new { error = "Hotfix admin accepts loopback requests only." }, cancellationToken).ConfigureAwait(false);
                return;
            }

            var path = context.Request.Url?.AbsolutePath ?? "";
            object response = (context.Request.HttpMethod, path) switch
            {
                ("GET", "/_lakona/hotfix/status") => await _controller.GetStatusAsync(cancellationToken).ConfigureAwait(false),
                ("POST", "/_lakona/hotfix/activate") => await _controller.ActivateAsync(await ReadJsonAsync<HotfixActivateRequest>(context.Request, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false),
                ("POST", "/_lakona/hotfix/rollback") => await _controller.RollbackAsync(cancellationToken).ConfigureAwait(false),
                ("POST", "/_lakona/hotfix/reload") => await _controller.ReloadAsync(cancellationToken).ConfigureAwait(false),
                _ => throw new FileNotFoundException("Unknown hotfix admin endpoint.")
            };

            await WriteJsonAsync(context.Response, response, cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException exception)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await WriteJsonAsync(context.Response, new { error = exception.Message }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await WriteJsonAsync(context.Response, new { error = exception.Message }, cancellationToken).ConfigureAwait(false);
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

    private static async Task<T> ReadJsonAsync<T>(HttpListenerRequest request, CancellationToken cancellationToken)
    {
        var value = await JsonSerializer.DeserializeAsync<T>(
            request.InputStream,
            HotfixAdminJson.Options,
            cancellationToken).ConfigureAwait(false);
        return value ?? throw new InvalidOperationException("Request body is required.");
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, object value, CancellationToken cancellationToken)
    {
        response.ContentType = "application/json";
        var json = JsonSerializer.Serialize(value, HotfixAdminJson.Options);
        var bytes = Encoding.UTF8.GetBytes(json);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    private static class StatusCodes
    {
        public const int Status400BadRequest = 400;
        public const int Status403Forbidden = 403;
        public const int Status404NotFound = 404;
    }
}
