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
                Timeout = TimeSpan.FromMilliseconds(500)
            };

            var body = await WaitForBodyAsync(
                http,
                $"http://127.0.0.1:{port}/_lakona/health/live",
                TestContext.Current.CancellationToken);

            Assert.Contains("\"status\": \"ok\"", body, StringComparison.Ordinal);
        }
        finally
        {
            await service.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    private static async Task<string> WaitForBodyAsync(
        HttpClient http,
        string url,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                return await http.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                lastError = ex;
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new TimeoutException($"Health endpoint did not respond at {url}.", lastError);
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
