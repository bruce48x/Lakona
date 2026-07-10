using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Health;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lakona.Game.Server.Tests.Health;

public sealed class LakonaHealthHttpHostedServiceTests
{
    [Fact]
    public async Task Enabled_health_host_serves_liveness_without_http_listener_urlacl()
    {
        var port = GetFreePort();
        var service = new LakonaHealthHttpHostedService(
            new LakonaGameRuntimeOptions
            {
                Health = new LakonaHealthOptions
                {
                    Http = new LakonaHealthHttpOptions
                    {
                        Enabled = true,
                        Host = "127.0.0.1",
                        Port = port,
                        RequireLoopback = true
                    }
                }
            },
            new LakonaHealthHttpRouter([LakonaHealthHttpRoutes.Live()]),
            NullLogger<LakonaHealthHttpHostedService>.Instance);

        await service.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            using var http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(5)
            };

            var body = await http.GetStringAsync(
                $"http://127.0.0.1:{port}/_lakona/health/live",
                TestContext.Current.CancellationToken);

            Assert.Contains("\"status\": \"ok\"", body, StringComparison.Ordinal);
        }
        finally
        {
            await service.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task StartAsync_propagates_health_listener_bind_failure()
    {
        var blocker = new TcpListener(IPAddress.Loopback, 0);
        blocker.Start();
        try
        {
            var port = ((IPEndPoint)blocker.LocalEndpoint).Port;
            var service = CreateHealthService(enabled: true, port: port);

            await Assert.ThrowsAnyAsync<SocketException>(() =>
                service.StartAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            blocker.Stop();
        }
    }

    [Fact]
    public async Task StartAsync_completes_when_health_listener_is_disabled()
    {
        var service = CreateHealthService(enabled: false, port: GetFreePort());

        await service.StartAsync(TestContext.Current.CancellationToken);
        await service.StopAsync(CancellationToken.None);
    }

    private static LakonaHealthHttpHostedService CreateHealthService(bool enabled, int port)
    {
        return new LakonaHealthHttpHostedService(
            new LakonaGameRuntimeOptions
            {
                Health = new LakonaHealthOptions
                {
                    Http = new LakonaHealthHttpOptions
                    {
                        Enabled = enabled,
                        Host = "127.0.0.1",
                        Port = port,
                        RequireLoopback = true
                    }
                }
            },
            new LakonaHealthHttpRouter([LakonaHealthHttpRoutes.Live()]),
            NullLogger<LakonaHealthHttpHostedService>.Instance);
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
