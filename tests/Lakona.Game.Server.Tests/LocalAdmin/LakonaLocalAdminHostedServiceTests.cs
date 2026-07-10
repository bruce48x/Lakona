using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using Lakona.Game.Server.LocalAdmin;
using Lakona.Game.Server.Observability;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lakona.Game.Server.Tests.LocalAdmin;

public sealed class LakonaLocalAdminHostedServiceTests
{
    [Theory]
    [InlineData("127.0.0.1", 20090, "http://127.0.0.1:20090/")]
    [InlineData("localhost", 20090, "http://localhost:20090/")]
    [InlineData("::1", 20090, "http://[::1]:20090/")]
    public void FormatPrefix_brackets_ipv6_hosts(string host, int port, string expected)
    {
        Assert.Equal(expected, LakonaLocalAdminHostedService.FormatPrefixForTesting(host, port));
    }

    [Fact]
    public async Task Request_tracker_waits_for_in_flight_handlers_to_finish()
    {
        var tracker = new LakonaLocalAdminRequestTracker();
        var unblock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = false;

        _ = tracker.Track(async () =>
        {
            await unblock.Task;
            observed = true;
        });

        var drainTask = tracker.DrainAsync(TestContext.Current.CancellationToken);
        Assert.False(drainTask.IsCompleted);

        unblock.SetResult();
        await drainTask;

        Assert.True(observed);
    }

    [Fact]
    public async Task StartAsync_returns_after_local_admin_listener_is_bound()
    {
        var listener = new TestLocalAdminListener();
        var port = GetFreePort();
        var service = CreateLocalAdminService(
            enabled: true,
            host: "127.0.0.1",
            port: port,
            listener: listener);

        await service.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            Assert.True(listener.IsListening);
            Assert.Equal($"http://127.0.0.1:{port}/", listener.Prefix);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task StartAsync_completes_when_local_admin_is_disabled()
    {
        var service = CreateLocalAdminService(
            enabled: false,
            host: "127.0.0.1",
            port: GetFreePort());

        await service.StartAsync(TestContext.Current.CancellationToken);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_propagates_local_admin_listener_bind_failure()
    {
        var listener = new TestLocalAdminListener(
            new InvalidOperationException("bind failed"));
        var service = CreateLocalAdminService(
            enabled: true,
            host: "127.0.0.1",
            port: GetFreePort(),
            listener: listener);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.StartAsync(TestContext.Current.CancellationToken));

        Assert.Equal("bind failed", exception.Message);
    }

    private static LakonaLocalAdminHostedService CreateLocalAdminService(
        bool enabled,
        string host,
        int port,
        ILakonaLocalAdminListener? listener = null)
    {
        return new LakonaLocalAdminHostedService(
            new LakonaObservabilityOptions
            {
                LocalAdmin = new LakonaLocalAdminObservabilityOptions
                {
                    Enabled = enabled,
                    EffectiveEnabled = enabled,
                    Host = host,
                    Port = port,
                    RequireLoopback = true
                }
            },
            new LakonaLocalAdminRouter([]),
            NullLogger<LakonaLocalAdminHostedService>.Instance,
            new LakonaLocalAdminRequestTracker(),
            listener is null ? null : () => listener);
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

    private sealed class TestLocalAdminListener(Exception? startException = null)
        : ILakonaLocalAdminListener
    {
        public bool IsListening { get; private set; }

        public string Prefix { get; private set; } = "";

        public void AddPrefix(string prefix)
        {
            Prefix = prefix;
        }

        public void Start()
        {
            if (startException is not null)
            {
                throw startException;
            }

            IsListening = true;
        }

        public async Task<HttpListenerContext> GetContextAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new UnreachableException();
        }

        public void Close()
        {
            IsListening = false;
        }
    }
}
