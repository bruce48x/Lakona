using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Health;
using Lakona.Game.Server.InternalHttp;
using Lakona.Game.Server.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
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

    [Fact]
    public async Task Health_registration_injects_router_logger()
    {
        var logger = new RecordingLogger<LakonaHttpRouter>();
        var services = new ServiceCollection();
        services.AddSingleton(new LakonaGameRuntimeOptions
        {
            Health = new LakonaHealthOptions
            {
                Http = new LakonaHealthHttpOptions { Enabled = true }
            }
        });
        services.AddSingleton(LakonaObservabilityOptions.Defaults());
        services.AddSingleton<ILogger<LakonaHttpRouter>>(logger);
        services.AddLakonaGameHealth();
        services.RemoveAll<ILakonaHealthHttpRoute>();
        services.AddSingleton<ILakonaHealthHttpRoute>(new ThrowingHealthRoute());
        using var provider = services.BuildServiceProvider();
        var router = provider.GetRequiredService<LakonaHttpRouter>();

        var response = await router.RouteAsync(
            new LakonaHttpRequest("GET", "/throw", Stream.Null, RemoteAddressIsLoopback: true),
            TestContext.Current.CancellationToken);

        Assert.Equal(400, response.StatusCode);
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Error && entry.Exception is InvalidOperationException);
    }

    [Fact]
    public async Task Health_registration_omits_health_routes_when_health_http_is_disabled()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new LakonaGameRuntimeOptions
        {
            Health = new LakonaHealthOptions
            {
                Http = new LakonaHealthHttpOptions { Enabled = false }
            }
        });
        services.AddSingleton(new LakonaObservabilityOptions
        {
            LocalAdmin = new LakonaLocalAdminObservabilityOptions
            {
                Enabled = true,
                EffectiveEnabled = true
            }
        });
        services.AddLakonaGameHealth();
        services.RemoveAll<ILakonaHealthHttpRoute>();
        services.AddSingleton<ILakonaHealthHttpRoute>(new TestHealthRoute());
        using var provider = services.BuildServiceProvider();
        var router = provider.GetRequiredService<LakonaHttpRouter>();

        var response = await router.RouteAsync(
            new LakonaHttpRequest(
                "GET",
                "/health-disabled",
                Stream.Null,
                RemoteAddressIsLoopback: true),
            TestContext.Current.CancellationToken);

        Assert.Equal(404, response.StatusCode);
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

    private sealed class ThrowingHealthRoute : ILakonaHealthHttpRoute
    {
        public string Method => "GET";
        public string Path => "/throw";

        public ValueTask<LakonaHealthHttpResponse> HandleAsync(
            LakonaHealthHttpRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("route failed");
        }
    }

    private sealed class TestHealthRoute : ILakonaHealthHttpRoute
    {
        public string Method => "GET";
        public string Path => "/health-disabled";

        public ValueTask<LakonaHealthHttpResponse> HandleAsync(
            LakonaHealthHttpRequest request,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<LakonaHealthHttpResponse>(
                new LakonaHealthHttpResponse(200, "text/plain", "unexpected"));
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, exception));
        }
    }

    private sealed record LogEntry(LogLevel Level, Exception? Exception);
}
