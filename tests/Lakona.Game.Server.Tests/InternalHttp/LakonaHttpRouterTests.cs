using Lakona.Game.Server.InternalHttp;
using Xunit;

namespace Lakona.Game.Server.Tests.InternalHttp;

public sealed class LakonaHttpRouterTests
{
    [Fact]
    public async Task Route_with_required_loopback_rejects_remote_client_without_dispatch()
    {
        var route = new TestRoute(requireLoopback: true);
        var response = await new LakonaHttpRouter([route]).RouteAsync(
            new LakonaHttpRequest("GET", "/test", Stream.Null, RemoteAddressIsLoopback: false),
            TestContext.Current.CancellationToken);

        Assert.Equal(403, response.StatusCode);
        Assert.False(route.WasCalled);
    }

    [Fact]
    public async Task Route_dispatches_an_allowed_request()
    {
        var route = new TestRoute(requireLoopback: true);
        var response = await new LakonaHttpRouter([route]).RouteAsync(
            new LakonaHttpRequest("get", "/test", Stream.Null, RemoteAddressIsLoopback: true),
            TestContext.Current.CancellationToken);

        Assert.Equal(200, response.StatusCode);
        Assert.True(route.WasCalled);
    }

    private sealed class TestRoute(bool requireLoopback) : ILakonaHttpRoute
    {
        public string Method => "GET";
        public string Path => "/test";
        public bool RequireLoopback => requireLoopback;
        public bool WasCalled { get; private set; }

        public ValueTask<LakonaHttpResponse> HandleAsync(
            LakonaHttpRequest request,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return new ValueTask<LakonaHttpResponse>(LakonaHttpResponse.Json(new { status = "ok" }));
        }
    }
}
