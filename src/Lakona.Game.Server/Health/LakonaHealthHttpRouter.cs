using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lakona.Game.Server.Health;

public sealed class LakonaHealthHttpRouter
{
    private readonly IReadOnlyDictionary<RouteKey, ILakonaHealthHttpRoute> _routes;
    private readonly ILogger<LakonaHealthHttpRouter> _logger;

    public LakonaHealthHttpRouter(IEnumerable<ILakonaHealthHttpRoute> routes)
        : this(routes, NullLogger<LakonaHealthHttpRouter>.Instance)
    {
    }

    public LakonaHealthHttpRouter(
        IEnumerable<ILakonaHealthHttpRoute> routes,
        ILogger<LakonaHealthHttpRouter> logger)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(logger);

        var map = new Dictionary<RouteKey, ILakonaHealthHttpRoute>();
        foreach (var route in routes)
        {
            var key = new RouteKey(NormalizeMethod(route.Method), route.Path);
            if (!map.TryAdd(key, route))
            {
                throw new InvalidOperationException(
                    $"Duplicate health route '{key.Method} {key.Path}' was registered.");
            }
        }

        _routes = map;
        _logger = logger;
    }

    public async ValueTask<LakonaHealthHttpResponse> RouteAsync(
        LakonaHealthHttpRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.RequireLoopback && !request.RemoteAddressIsLoopback)
        {
            return LakonaHealthHttpResponse.Json(
                new { error = "Health endpoint accepts loopback requests only." },
                403);
        }

        var key = new RouteKey(NormalizeMethod(request.Method), request.Path);
        if (!_routes.TryGetValue(key, out var route))
        {
            return LakonaHealthHttpResponse.Json(
                new { error = "Unknown health endpoint." },
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
                "Health route failed for {Method} {Path}.",
                key.Method,
                key.Path);
            return LakonaHealthHttpResponse.Json(new { error = "Health endpoint failed." }, 400);
        }
    }

    private static string NormalizeMethod(string method)
    {
        return method.ToUpperInvariant();
    }

    private readonly record struct RouteKey(string Method, string Path);
}
