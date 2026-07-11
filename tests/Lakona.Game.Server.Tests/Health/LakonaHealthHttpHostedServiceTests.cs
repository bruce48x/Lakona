using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Health;
using Lakona.Game.Server.InternalHttp;
using Lakona.Game.Server.LocalAdmin;
using Lakona.Game.Server.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
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
            LakonaObservabilityOptions.Defaults(),
            new LakonaHttpRouter([new LakonaHealthHttpRouteAdapter(LakonaHealthHttpRoutes.Live(), true)]),
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

    [Fact]
    public async Task Enabled_local_admin_starts_shared_listener_when_health_http_is_disabled()
    {
        var port = GetFreePort();
        var observability = new LakonaObservabilityOptions
        {
            LocalAdmin = new LakonaLocalAdminObservabilityOptions
            {
                Enabled = true,
                EffectiveEnabled = true
            }
        };
        var service = new LakonaHealthHttpHostedService(
            new LakonaGameRuntimeOptions
            {
                Health = new LakonaHealthOptions
                {
                    Http = new LakonaHealthHttpOptions
                    {
                        Enabled = false,
                        Host = "127.0.0.1",
                        Port = port
                    }
                }
            },
            observability,
            new LakonaHttpRouter([new TestLocalAdminRoute()]),
            NullLogger<LakonaHealthHttpHostedService>.Instance);

        await service.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var body = await http.GetStringAsync(
                $"http://127.0.0.1:{port}/_lakona/test",
                TestContext.Current.CancellationToken);

            Assert.Equal("ok", body);
        }
        finally
        {
            await service.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Explicit_observability_options_start_shared_listener_when_health_http_is_disabled()
    {
        var port = GetFreePort();
        var services = new ServiceCollection();
        services.AddSingleton(new LakonaGameRuntimeOptions
        {
            Health = new LakonaHealthOptions
            {
                Http = new LakonaHealthHttpOptions
                {
                    Enabled = false,
                    Host = "127.0.0.1",
                    Port = port
                }
            }
        });
        services.AddLakonaGameObservability(new LakonaObservabilityOptions
        {
            LocalAdmin = new LakonaLocalAdminObservabilityOptions
            {
                Enabled = true,
                EffectiveEnabled = true
            }
        });
        services.RemoveAll<ILakonaLocalAdminRoute>();
        services.AddSingleton<ILakonaLocalAdminRoute>(new TestLocalAdminRoute());
        services.AddLakonaGameHealth();
        services.RemoveAll<ILakonaHealthHttpRoute>();
        await using var provider = services.BuildServiceProvider();
        var service = provider.GetServices<IHostedService>().OfType<LakonaHealthHttpHostedService>().Single();

        await service.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var body = await http.GetStringAsync(
                $"http://127.0.0.1:{port}/_lakona/test",
                TestContext.Current.CancellationToken);

            Assert.Equal("ok", body);
        }
        finally
        {
            await service.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Request_body_that_exceeds_remaining_buffer_capacity_returns_bad_request()
    {
        const int headerLength = 16 * 1024 - 4;
        var port = GetFreePort();
        var service = CreateHealthService(enabled: true, port: port);

        await service.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            const string prefix = "POST /_lakona/test HTTP/1.1\r\nContent-Length: 1\r\nX-Fill: ";
            var requestHeaders = prefix + new string('a', headerLength - prefix.Length) + "\r\n\r\n";
            var request = Encoding.ASCII.GetBytes(requestHeaders);

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port, TestContext.Current.CancellationToken);
            await using var stream = client.GetStream();
            await stream.WriteAsync(request, TestContext.Current.CancellationToken);
            client.Client.Shutdown(SocketShutdown.Send);

            using var reader = new StreamReader(stream, Encoding.ASCII);
            var response = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

            Assert.StartsWith("HTTP/1.1 400 Bad Request", response, StringComparison.Ordinal);
        }
        finally
        {
            await service.StopAsync(TestContext.Current.CancellationToken);
        }
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
            LakonaObservabilityOptions.Defaults(),
            new LakonaHttpRouter([new LakonaHealthHttpRouteAdapter(LakonaHealthHttpRoutes.Live(), true)]),
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

    private sealed class TestLocalAdminRoute : ILakonaHttpRoute, ILakonaLocalAdminRoute
    {
        public string Method => "GET";
        public string Path => "/_lakona/test";
        public bool RequireLoopback => true;

        public ValueTask<LakonaHttpResponse> HandleAsync(
            LakonaHttpRequest request,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<LakonaHttpResponse>(new LakonaHttpResponse(200, "text/plain", "ok"));
        }

        ValueTask<LakonaLocalAdminResponse> ILakonaLocalAdminRoute.HandleAsync(
            LakonaLocalAdminRequest request,
            CancellationToken cancellationToken)
        {
            return new ValueTask<LakonaLocalAdminResponse>(
                new LakonaLocalAdminResponse(200, "text/plain", "ok"));
        }
    }
}
