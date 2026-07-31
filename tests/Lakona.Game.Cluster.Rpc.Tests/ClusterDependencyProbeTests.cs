using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Rpc.Core;
using Xunit;

namespace Lakona.Game.Cluster.Rpc.Tests;

public sealed class ClusterDependencyProbeTests
{
    [Fact]
    public async Task CheckRouteDirectoryReturnsHealthyWhenResolveCompletes()
    {
        var probe = new ClusterDependencyProbe(
            new StaticClientFactory(new ResolvingClient()),
            TimeSpan.FromSeconds(1));

        var health = await probe.CheckRouteDirectoryAsync(
            NewDirectoryLocation(),
            TestContext.Current.CancellationToken);

        Assert.Equal("route-directory", health.Name);
        Assert.Equal(ClusterDependencyStatus.Healthy, health.Status);
        Assert.Null(health.Error);
    }

    [Fact]
    public async Task CheckRouteDirectoryReturnsTimeoutWithoutHanging()
    {
        var probe = new ClusterDependencyProbe(
            new StaticClientFactory(new HangingClient()),
            TimeSpan.FromMilliseconds(1));

        var health = await probe.CheckRouteDirectoryAsync(
            NewDirectoryLocation(),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterDependencyStatus.Timeout, health.Status);
        Assert.NotNull(health.Error);
    }

    [Fact]
    public async Task CheckRouteDirectoryPropagatesCallerCancellation()
    {
        var probe = new ClusterDependencyProbe(
            new StaticClientFactory(new HangingClient()),
            TimeSpan.FromSeconds(1));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        var exception = await Record.ExceptionAsync(async () =>
            await probe.CheckRouteDirectoryAsync(NewDirectoryLocation(), canceled.Token));

        Assert.IsAssignableFrom<OperationCanceledException>(exception);
    }

    [Fact]
    public async Task CheckRouteDirectoryReturnsUnhealthyWhenClientFactoryFails()
    {
        var probe = new ClusterDependencyProbe(
            new ThrowingClientFactory(new InvalidOperationException("connect failed")),
            TimeSpan.FromSeconds(1));

        var health = await probe.CheckRouteDirectoryAsync(
            NewDirectoryLocation(),
            TestContext.Current.CancellationToken);

        Assert.Equal(ClusterDependencyStatus.Unhealthy, health.Status);
        Assert.Contains("connect failed", health.Error, StringComparison.Ordinal);
    }

    private static RouteLocation NewDirectoryLocation() =>
        new(
            "directory",
            "directory",
            new NodeEndpoint("tcp://127.0.0.1:21001"),
            DateTimeOffset.UtcNow.AddMinutes(1),
            nodeEpoch: 1,
            generation: 1);

    private sealed class StaticClientFactory(IRpcClient client) : IClusterClientFactory
    {
        public ValueTask<IRpcClient> GetClientAsync(
            RouteLocation target,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(client);
        }
    }

    private sealed class ThrowingClientFactory(Exception exception) : IClusterClientFactory
    {
        public ValueTask<IRpcClient> GetClientAsync(
            RouteLocation target,
            CancellationToken cancellationToken = default) => throw exception;
    }

    private sealed class ResolvingClient : IRpcClient
    {
        public ValueTask<TResult> CallAsync<TArg, TResult>(
            RpcMethod<TArg, TResult> method,
            TArg? arg,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            object reply = new RouteResolveReply();
            return ValueTask.FromResult((TResult)reply);
        }

        public void RegisterNotificationHandler<TArg>(
            RpcNotificationMethod<TArg> method,
            Func<TArg, ValueTask> handler)
        {
        }
    }

    private sealed class HangingClient : IRpcClient
    {
        public async ValueTask<TResult> CallAsync<TArg, TResult>(
            RpcMethod<TArg, TResult> method,
            TArg? arg,
            CancellationToken ct)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("unreachable");
        }

        public void RegisterNotificationHandler<TArg>(
            RpcNotificationMethod<TArg> method,
            Func<TArg, ValueTask> handler)
        {
        }
    }
}
