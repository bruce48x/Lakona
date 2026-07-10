using System.Diagnostics;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Hosting;
using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.Json;
using Lakona.Rpc.Server;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Server.Tests.Hosting;

public sealed class RpcServersHostedServiceTests
{
    [Fact]
    public async Task StartAsync_waits_until_every_rpc_acceptor_is_listening()
    {
        var first = new DelayedConfigurator("first");
        var second = new DelayedConfigurator("second");
        await using var services = new ServiceCollection()
            .AddSingleton(new LakonaGameRuntimeOptions())
            .BuildServiceProvider();
        var hosted = new RpcServersHostedService([first, second], services);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var start = hosted.StartAsync(cts.Token);
        await Task.Yield();
        Assert.False(start.IsCompleted);

        first.Release("test://first");
        await Task.Yield();
        Assert.False(start.IsCompleted);

        second.Release("test://second");
        await start;
        await hosted.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_propagates_acceptor_creation_failure()
    {
        await using var services = new ServiceCollection()
            .AddSingleton(new LakonaGameRuntimeOptions())
            .BuildServiceProvider();
        var hosted = new RpcServersHostedService([new FailingConfigurator()], services);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            hosted.StartAsync(TestContext.Current.CancellationToken));

        Assert.Equal("bind failed", exception.Message);
    }

    [Fact]
    public async Task StartAsync_completes_when_no_rpc_configurators_exist()
    {
        await using var services = new ServiceCollection()
            .AddSingleton(new LakonaGameRuntimeOptions())
            .BuildServiceProvider();
        var hosted = new RpcServersHostedService([], services);

        await hosted.StartAsync(TestContext.Current.CancellationToken);
        await hosted.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_cancels_without_hanging_before_listener_readiness()
    {
        var configurator = new DelayedConfigurator("delayed");
        await using var services = new ServiceCollection()
            .AddSingleton(new LakonaGameRuntimeOptions())
            .BuildServiceProvider();
        var hosted = new RpcServersHostedService([configurator], services);
        using var cts = new CancellationTokenSource();

        var start = hosted.StartAsync(cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
        await hosted.StopAsync(CancellationToken.None);
    }

    private sealed class DelayedConfigurator(string transport) : IRpcServerConfigurator
    {
        private readonly TaskCompletionSource<IRpcConnectionAcceptor> _acceptor = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public string Transport { get; } = transport;

        public void Configure(LakonaGameServerRpcContext context)
        {
            context.Builder
                .UseSerializer(new JsonRpcSerializer())
                .ConfigureServices(_ => { })
                .UseAcceptor(ct => new ValueTask<IRpcConnectionAcceptor>(
                    _acceptor.Task.WaitAsync(ct)));
        }

        public void Release(string listenAddress)
        {
            _acceptor.TrySetResult(new BlockingConnectionAcceptor(listenAddress));
        }
    }

    private sealed class FailingConfigurator : IRpcServerConfigurator
    {
        public string Transport => "failing";

        public void Configure(LakonaGameServerRpcContext context)
        {
            context.Builder
                .UseSerializer(new JsonRpcSerializer())
                .ConfigureServices(_ => { })
                .UseAcceptor(_ => throw new InvalidOperationException("bind failed"));
        }
    }

    private sealed class BlockingConnectionAcceptor(string listenAddress) : IRpcConnectionAcceptor
    {
        public string ListenAddress { get; } = listenAddress;

        public async ValueTask<RpcAcceptedConnection> AcceptAsync(CancellationToken ct = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new UnreachableException();
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
