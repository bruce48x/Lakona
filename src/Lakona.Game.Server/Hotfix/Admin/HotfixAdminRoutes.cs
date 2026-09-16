using System.Text.Json;
using Lakona.Game.Server.LocalAdmin;

namespace Lakona.Game.Server.HotfixAdmin;

internal sealed class HotfixAdminStatusRoute : ILakonaLocalAdminRoute
{
    private readonly HotfixAdminController _controller;

    public HotfixAdminStatusRoute(HotfixAdminController controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
    }

    public string Method => "GET";

    public string Path => "/_lakona/hotfix/status";

    public async ValueTask<LakonaLocalAdminResponse> HandleAsync(
        LakonaLocalAdminRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _controller.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        return LakonaLocalAdminResponse.Json(response, options: HotfixAdminJson.Options);
    }
}

internal sealed class HotfixAdminActivateRoute : ILakonaLocalAdminRoute
{
    private readonly HotfixAdminController _controller;

    public HotfixAdminActivateRoute(HotfixAdminController controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
    }

    public string Method => "POST";

    public string Path => "/_lakona/hotfix/activate";

    public async ValueTask<LakonaLocalAdminResponse> HandleAsync(
        LakonaLocalAdminRequest request,
        CancellationToken cancellationToken = default)
    {
        return await HotfixAdminRouteResponse.ExecuteAsync(async () =>
        {
            HotfixActivateRequest? activateRequest;
            try
            {
                activateRequest = await JsonSerializer.DeserializeAsync<HotfixActivateRequest>(
                    request.Body, HotfixAdminJson.Options, cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException)
            {
                throw _controller.InvalidRequest();
            }
            if (activateRequest is null || string.IsNullOrWhiteSpace(activateRequest.Version))
                throw _controller.InvalidRequest();
            return await _controller.ActivateAsync(activateRequest, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }
}

internal sealed class HotfixAdminRollbackRoute : ILakonaLocalAdminRoute
{
    private readonly HotfixAdminController _controller;

    public HotfixAdminRollbackRoute(HotfixAdminController controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
    }

    public string Method => "POST";

    public string Path => "/_lakona/hotfix/rollback";

    public async ValueTask<LakonaLocalAdminResponse> HandleAsync(
        LakonaLocalAdminRequest request,
        CancellationToken cancellationToken = default)
    {
        return await HotfixAdminRouteResponse.ExecuteAsync(
            () => _controller.RollbackAsync(cancellationToken)).ConfigureAwait(false);
    }
}

internal sealed class HotfixAdminReloadRoute : ILakonaLocalAdminRoute
{
    private readonly HotfixAdminController _controller;

    public HotfixAdminReloadRoute(HotfixAdminController controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
    }

    public string Method => "POST";

    public string Path => "/_lakona/hotfix/reload";

    public async ValueTask<LakonaLocalAdminResponse> HandleAsync(
        LakonaLocalAdminRequest request,
        CancellationToken cancellationToken = default)
    {
        return await HotfixAdminRouteResponse.ExecuteAsync(
            () => _controller.ReloadAsync(cancellationToken)).ConfigureAwait(false);
    }
}

internal static class HotfixAdminRouteResponse
{
    public static async ValueTask<LakonaLocalAdminResponse> ExecuteAsync(Func<Task<HotfixStatusResponse>> operation)
    {
        try
        {
            return LakonaLocalAdminResponse.Json(await operation().ConfigureAwait(false), options: HotfixAdminJson.Options);
        }
        catch (HotfixAdminException exception)
        {
            return LakonaLocalAdminResponse.Json(new { error = exception.Message, diagnostic = exception.Diagnostic },
                400, HotfixAdminJson.Options);
        }
    }
}
