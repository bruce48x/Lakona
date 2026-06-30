using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lakona.Game.Server.LocalAdmin;

public sealed class LakonaLocalAdminRouter
{
    private readonly IReadOnlyDictionary<RouteKey, ILakonaLocalAdminRoute> _routes;
    private readonly ILogger<LakonaLocalAdminRouter> _logger;

    public LakonaLocalAdminRouter(IEnumerable<ILakonaLocalAdminRoute> routes)
        : this(routes, NullLogger<LakonaLocalAdminRouter>.Instance)
    {
    }

    public LakonaLocalAdminRouter(
        IEnumerable<ILakonaLocalAdminRoute> routes,
        ILogger<LakonaLocalAdminRouter> logger)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(logger);

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
        _logger = logger;
    }

    public async ValueTask<LakonaLocalAdminResponse> RouteAsync(
        LakonaLocalAdminRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.RequireLoopback && !request.RemoteAddressIsLoopback)
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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Local admin route failed for {Method} {Path}.",
                key.Method,
                key.Path);
            return LakonaLocalAdminResponse.Json(new { error = "Local admin endpoint failed." }, 400);
        }
    }

    private static string NormalizeMethod(string method)
    {
        return method.ToUpperInvariant();
    }

    private readonly record struct RouteKey(string Method, string Path);
}
