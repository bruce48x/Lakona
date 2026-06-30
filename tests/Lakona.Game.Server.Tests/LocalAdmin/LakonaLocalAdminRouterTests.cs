using System.Text.Json;
using Lakona.Game.Server.LocalAdmin;
using Microsoft.Extensions.Logging;
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
            new LakonaLocalAdminRequest("GET", "/_lakona/test", Stream.Null, RemoteAddressIsLoopback: true),
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
        var body = new CountingStream();

        var response = await router.RouteAsync(
            new LakonaLocalAdminRequest("GET", "/_lakona/test", body, RemoteAddressIsLoopback: false),
            TestContext.Current.CancellationToken);

        Assert.Equal(403, response.StatusCode);
        Assert.Empty(route.Requests);
        Assert.Equal(0, body.ReadCount);
    }

    [Fact]
    public async Task Unknown_route_returns_404()
    {
        var router = new LakonaLocalAdminRouter(
            [new TestRoute("GET", "/_lakona/test", LakonaLocalAdminResponse.Json(new { ok = true }))]);
        var body = new CountingStream();

        var response = await router.RouteAsync(
            new LakonaLocalAdminRequest("GET", "/_lakona/missing", body, RemoteAddressIsLoopback: true),
            TestContext.Current.CancellationToken);

        Assert.Equal(404, response.StatusCode);
        Assert.Equal(0, body.ReadCount);
    }

    [Fact]
    public async Task Route_exception_returns_400_with_generic_json_error()
    {
        var logger = new RecordingLogger<LakonaLocalAdminRouter>();
        var router = new LakonaLocalAdminRouter([new ThrowingRoute()], logger);

        var response = await router.RouteAsync(
            new LakonaLocalAdminRequest("POST", "/_lakona/throw", Stream.Null, RemoteAddressIsLoopback: true),
            TestContext.Current.CancellationToken);

        Assert.Equal(400, response.StatusCode);
        Assert.Equal("application/json", response.ContentType);
        using var document = JsonDocument.Parse(response.Body);
        Assert.Equal("Local admin endpoint failed.", document.RootElement.GetProperty("error").GetString());
        Assert.DoesNotContain("route failed secret-token", response.Body, StringComparison.Ordinal);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.IsType<InvalidOperationException>(entry.Exception);
        Assert.Contains("POST", entry.Message, StringComparison.Ordinal);
        Assert.Contains("/_lakona/throw", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("route failed secret-token", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Method_matching_is_case_insensitive()
    {
        var route = new TestRoute("GET", "/_lakona/test", new LakonaLocalAdminResponse(204, "text/plain", ""));
        var router = new LakonaLocalAdminRouter([route]);

        var response = await router.RouteAsync(
            new LakonaLocalAdminRequest("get", "/_lakona/test", Stream.Null, RemoteAddressIsLoopback: true),
            TestContext.Current.CancellationToken);

        Assert.Equal(204, response.StatusCode);
        Assert.Single(route.Requests);
    }

    [Fact]
    public async Task Explicit_loopback_policy_false_allows_non_loopback_dispatch()
    {
        var route = new TestRoute("GET", "/_lakona/test", new LakonaLocalAdminResponse(200, "text/plain", "ok"));
        var router = new LakonaLocalAdminRouter([route]);

        var response = await router.RouteAsync(
            new LakonaLocalAdminRequest(
                "GET",
                "/_lakona/test",
                Stream.Null,
                RemoteAddressIsLoopback: false,
                RequireLoopback: false),
            TestContext.Current.CancellationToken);

        Assert.Equal(200, response.StatusCode);
        Assert.Single(route.Requests);
    }

    [Fact]
    public void Duplicate_routes_throw_during_router_construction()
    {
        var first = new TestRoute("GET", "/_lakona/test", LakonaLocalAdminResponse.Json(new { ok = true }));
        var second = new TestRoute("get", "/_lakona/test", LakonaLocalAdminResponse.Json(new { ok = true }));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new LakonaLocalAdminRouter([first, second]));

        Assert.Contains("Duplicate", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/_lakona/test", exception.Message, StringComparison.Ordinal);
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
            throw new InvalidOperationException("route failed secret-token");
        }
    }

    private sealed class CountingStream : MemoryStream
    {
        public int ReadCount { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ReadCount++;
            return base.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            ReadCount++;
            return base.Read(buffer);
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return base.ReadAsync(buffer, cancellationToken);
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);
}
