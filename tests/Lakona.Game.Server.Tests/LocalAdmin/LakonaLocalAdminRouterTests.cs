using System.Text.Json;
using Lakona.Game.Server.LocalAdmin;
using Xunit;

namespace Lakona.Game.Server.Tests.LocalAdmin;

public sealed class LakonaLocalAdminRouterTests
{
    [Fact]
    public async Task Matching_method_and_path_dispatch_returns_route_response()
    {
        var route = new TestRoute("GET", "/_lakona/test", new LakonaLocalAdminResponse(202, "text/plain", "accepted"));
        var router = new LakonaLocalAdminRouter([route]);

        var response = await router.RouteAsync(
            new LakonaLocalAdminRequest("GET", "/_lakona/test", "", RemoteAddressIsLoopback: true),
            TestContext.Current.CancellationToken);

        Assert.Equal(202, response.StatusCode);
        Assert.Equal("text/plain", response.ContentType);
        Assert.Equal("accepted", response.Body);
        Assert.Single(route.Requests);
    }

    [Fact]
    public async Task Non_loopback_request_returns_403_without_dispatching_route()
    {
        var route = new TestRoute("GET", "/_lakona/test", LakonaLocalAdminResponse.Json(new { ok = true }));
        var router = new LakonaLocalAdminRouter([route]);

        var response = await router.RouteAsync(
            new LakonaLocalAdminRequest("GET", "/_lakona/test", "", RemoteAddressIsLoopback: false),
            TestContext.Current.CancellationToken);

        Assert.Equal(403, response.StatusCode);
        Assert.Empty(route.Requests);
    }

    [Fact]
    public async Task Unknown_route_returns_404()
    {
        var router = new LakonaLocalAdminRouter(
            [new TestRoute("GET", "/_lakona/test", LakonaLocalAdminResponse.Json(new { ok = true }))]);

        var response = await router.RouteAsync(
            new LakonaLocalAdminRequest("GET", "/_lakona/missing", "", RemoteAddressIsLoopback: true),
            TestContext.Current.CancellationToken);

        Assert.Equal(404, response.StatusCode);
    }

    [Fact]
    public async Task Route_exception_returns_400_with_json_error()
    {
        var router = new LakonaLocalAdminRouter([new ThrowingRoute()]);

        var response = await router.RouteAsync(
            new LakonaLocalAdminRequest("POST", "/_lakona/throw", "{}", RemoteAddressIsLoopback: true),
            TestContext.Current.CancellationToken);

        Assert.Equal(400, response.StatusCode);
        Assert.Equal("application/json", response.ContentType);
        using var document = JsonDocument.Parse(response.Body);
        Assert.Equal("route failed", document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Method_matching_is_case_insensitive()
    {
        var route = new TestRoute("GET", "/_lakona/test", new LakonaLocalAdminResponse(204, "text/plain", ""));
        var router = new LakonaLocalAdminRouter([route]);

        var response = await router.RouteAsync(
            new LakonaLocalAdminRequest("get", "/_lakona/test", "", RemoteAddressIsLoopback: true),
            TestContext.Current.CancellationToken);

        Assert.Equal(204, response.StatusCode);
        Assert.Single(route.Requests);
    }

    private sealed class TestRoute : ILakonaLocalAdminRoute
    {
        private readonly LakonaLocalAdminResponse _response;

        public TestRoute(string method, string path, LakonaLocalAdminResponse response)
        {
            Method = method;
            Path = path;
            _response = response;
        }

        public string Method { get; }

        public string Path { get; }

        public List<LakonaLocalAdminRequest> Requests { get; } = [];

        public ValueTask<LakonaLocalAdminResponse> HandleAsync(
            LakonaLocalAdminRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return new ValueTask<LakonaLocalAdminResponse>(_response);
        }
    }

    private sealed class ThrowingRoute : ILakonaLocalAdminRoute
    {
        public string Method => "POST";

        public string Path => "/_lakona/throw";

        public ValueTask<LakonaLocalAdminResponse> HandleAsync(
            LakonaLocalAdminRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("route failed");
        }
    }
}
