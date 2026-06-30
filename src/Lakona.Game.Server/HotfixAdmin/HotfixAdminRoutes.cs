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
        var activateRequest = JsonSerializer.Deserialize<HotfixActivateRequest>(
            request.Body,
            HotfixAdminJson.Options)
            ?? throw new InvalidOperationException("Request body is required.");
        var response = await _controller.ActivateAsync(activateRequest, cancellationToken).ConfigureAwait(false);
        return LakonaLocalAdminResponse.Json(response, options: HotfixAdminJson.Options);
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
        var response = await _controller.RollbackAsync(cancellationToken).ConfigureAwait(false);
        return LakonaLocalAdminResponse.Json(response, options: HotfixAdminJson.Options);
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
        var response = await _controller.ReloadAsync(cancellationToken).ConfigureAwait(false);
        return LakonaLocalAdminResponse.Json(response, options: HotfixAdminJson.Options);
    }
}
