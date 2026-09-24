using System.Net;
using System.Net.Sockets;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Health;
using Lakona.Game.Server.LocalAdmin;
using Lakona.Game.Server.Management;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Lakona.Game.Server.Tests.Management;

public sealed class LakonaManagementRoutingTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Loopback_policy_is_enforced_before_dispatch(bool health, bool requireLoopback)
    {
        var calls = 0;
        await using var app = Create(health, requireLoopback, () => calls++, out var address,
            simulateRemote: true);
        await app.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(5) };
            using var response = await client.GetAsync("/_lakona/test", TestContext.Current.CancellationToken);
            Assert.Equal(requireLoopback ? HttpStatusCode.Forbidden : HttpStatusCode.Accepted, response.StatusCode);
            Assert.Equal(requireLoopback ? 0 : 1, calls);
        }
        finally { await app.StopAsync(TestContext.Current.CancellationToken); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Kestrel_dispatches_routes_and_handles_unknown_paths_and_methods(bool health)
    {
        var calls = 0;
        await using var app = Create(health, true, () => calls++, out var address);
        await app.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(5) };
            using var success = await client.GetAsync("/_lakona/test", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Accepted, success.StatusCode);
            Assert.Equal("accepted", await success.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            using var missing = await client.GetAsync("/_lakona/missing", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            using var wrongMethod = await client.PostAsync("/_lakona/test", null, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.MethodNotAllowed, wrongMethod.StatusCode);
            Assert.Equal(1, calls);
        }
        finally { await app.StopAsync(TestContext.Current.CancellationToken); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Unexpected_handler_failure_returns_500_without_exception_details(bool health)
    {
        await using var app = Create(health, true, () => throw new InvalidOperationException("private-secret"), out var address);
        await app.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(5) };
            using var response = await client.GetAsync("/_lakona/test", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.DoesNotContain("private-secret", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }
        finally { await app.StopAsync(TestContext.Current.CancellationToken); }
    }

    private static WebApplication Create(bool health, bool requireLoopback, Action handle,
        out string address, bool simulateRemote = false)
    {
        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        address = $"http://127.0.0.1:{port}";
        var runtime = new LakonaGameRuntimeOptions
        {
            Health = new LakonaHealthOptions { Enabled = health, RequireLoopback = requireLoopback },
            Management = new LakonaManagementOptions
            {
                Http = new LakonaManagementHttpOptions { Host = "127.0.0.1", Port = port },
                Admin = new LakonaManagementAdminOptions { Enabled = !health, RequireLoopback = requireLoopback }
            }
        };
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(runtime);
        if (health) builder.Services.AddSingleton<ILakonaHealthHttpRoute>(new HealthRoute(handle));
        else builder.Services.AddSingleton<ILakonaLocalAdminRoute>(new AdminRoute(handle));
        LakonaHttpHosting.Configure(builder, runtime);
        var app = builder.Build();
        // Control only the peer address; routing and access policy run through production mapping.
        if (simulateRemote)
            app.Use((context, next) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.1");
                return next(context);
            });
        LakonaHttpHosting.Map(app);
        return app;
    }

    private sealed class HealthRoute(Action handle) : ILakonaHealthHttpRoute
    {
        public string Method => "GET";
        public string Path => "/_lakona/test";
        public ValueTask<LakonaHealthHttpResponse> HandleAsync(LakonaHealthHttpRequest request, CancellationToken cancellationToken)
        {
            handle();
            return new(new LakonaHealthHttpResponse(202, "text/plain", "accepted"));
        }
    }

    private sealed class AdminRoute(Action handle) : ILakonaLocalAdminRoute
    {
        public string Method => "GET";
        public string Path => "/_lakona/test";
        public ValueTask<LakonaLocalAdminResponse> HandleAsync(LakonaLocalAdminRequest request, CancellationToken cancellationToken)
        {
            handle();
            return new(new LakonaLocalAdminResponse(202, "text/plain", "accepted"));
        }
    }
}
