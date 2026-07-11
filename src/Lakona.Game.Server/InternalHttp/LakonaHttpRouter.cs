using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lakona.Game.Server.InternalHttp;

public sealed class LakonaHttpRouter
{
    private readonly IReadOnlyDictionary<RouteKey, ILakonaHttpRoute> _routes;
    private readonly ILogger<LakonaHttpRouter> _logger;

    public LakonaHttpRouter(IEnumerable<ILakonaHttpRoute> routes)
        : this(routes, NullLogger<LakonaHttpRouter>.Instance)
    {
    }

    public LakonaHttpRouter(IEnumerable<ILakonaHttpRoute> routes, ILogger<LakonaHttpRouter> logger)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(logger);
        var map = new Dictionary<RouteKey, ILakonaHttpRoute>();
        foreach (var route in routes)
        {
            var key = new RouteKey(route.Method.ToUpperInvariant(), route.Path);
            if (!map.TryAdd(key, route))
            {
                throw new InvalidOperationException($"Duplicate HTTP route '{key.Method} {key.Path}' was registered.");
            }
        }

        _routes = map;
        _logger = logger;
    }

    public async ValueTask<LakonaHttpResponse> RouteAsync(LakonaHttpRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var key = new RouteKey(request.Method.ToUpperInvariant(), request.Path);
        if (!_routes.TryGetValue(key, out var route))
        {
            return LakonaHttpResponse.Json(new { error = "Unknown local HTTP endpoint." }, 404);
        }

        if (route.RequireLoopback && !request.RemoteAddressIsLoopback)
        {
            return LakonaHttpResponse.Json(new { error = "Endpoint accepts loopback requests only." }, 403);
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
            _logger.LogError(exception, "Local HTTP route failed for {Method} {Path}.", key.Method, key.Path);
            return LakonaHttpResponse.Json(new { error = "Local HTTP endpoint failed." }, 400);
        }
    }

    private readonly record struct RouteKey(string Method, string Path);
}
