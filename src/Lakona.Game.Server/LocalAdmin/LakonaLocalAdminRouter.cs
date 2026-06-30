namespace Lakona.Game.Server.LocalAdmin;

public sealed class LakonaLocalAdminRouter
{
    private readonly IReadOnlyDictionary<RouteKey, ILakonaLocalAdminRoute> _routes;

    public LakonaLocalAdminRouter(IEnumerable<ILakonaLocalAdminRoute> routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var map = new Dictionary<RouteKey, ILakonaLocalAdminRoute>();
        foreach (var route in routes)
        {
            var key = new RouteKey(NormalizeMethod(route.Method), route.Path);
            if (!map.TryAdd(key, route))
            {
                throw new InvalidOperationException(
                    $"Duplicate local admin route '{key.Method} {key.Path}' was registered.");
            }
        }

        _routes = map;
    }

    public async ValueTask<LakonaLocalAdminResponse> RouteAsync(
        LakonaLocalAdminRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!request.RemoteAddressIsLoopback)
        {
            return LakonaLocalAdminResponse.Json(
                new { error = "Local admin accepts loopback requests only." },
                403);
        }

        var key = new RouteKey(NormalizeMethod(request.Method), request.Path);
        if (!_routes.TryGetValue(key, out var route))
        {
            return LakonaLocalAdminResponse.Json(
                new { error = "Unknown local admin endpoint." },
                404);
        }

        try
        {
            return await route.HandleAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return LakonaLocalAdminResponse.Json(new { error = exception.Message }, 400);
        }
    }

    private static string NormalizeMethod(string method)
    {
        return method.ToUpperInvariant();
    }

    private readonly record struct RouteKey(string Method, string Path);
}
