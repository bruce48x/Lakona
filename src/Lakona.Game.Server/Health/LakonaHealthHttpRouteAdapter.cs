using Lakona.Game.Server.InternalHttp;

namespace Lakona.Game.Server.Health;

public sealed class LakonaHealthHttpRouteAdapter(
    ILakonaHealthHttpRoute route,
    bool requireLoopback) : ILakonaHttpRoute
{
    public string Method => route.Method;
    public string Path => route.Path;
    public bool RequireLoopback => requireLoopback;

    public async ValueTask<LakonaHttpResponse> HandleAsync(
        LakonaHttpRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await route.HandleAsync(
            new LakonaHealthHttpRequest(request.Method, request.Path, request.RemoteAddressIsLoopback, RequireLoopback),
            cancellationToken).ConfigureAwait(false);
        return new LakonaHttpResponse(response.StatusCode, response.ContentType, response.Body);
    }
}
