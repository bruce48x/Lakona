using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Rpc.Core;
using Xunit;

namespace Lakona.Game.Cluster.Rpc.Tests;

public sealed class ULinkRpcClusterDependencyProbeTests
{
    [Fact]
    public async Task ProbeReportsNodeDirectoryDependency()
    {
        var nodeDirectory = new InMemoryNodeDirectory();
        var probe = ULinkRpcClusterDependencyProbe.ForNodeDirectory(
            nodeDirectory,
            "local",
            "node-a",
            TimeSpan.FromSeconds(1));

        var health = await probe.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal("node-directory", health.Name);
        Assert.Equal(ULinkRpcClusterDependencyStatus.Healthy, health.Status);
        Assert.Null(health.Error);
    }

    [Fact]
    public async Task CheckNodeDirectoryReturnsTimeoutWithoutHanging()
    {
        var probe = ULinkRpcClusterDependencyProbe.ForNodeDirectory(
            new HangingNodeDirectory(),
            "local",
            "node-a",
            TimeSpan.FromMilliseconds(1));

        var health = await probe.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal("node-directory", health.Name);
        Assert.Equal(ULinkRpcClusterDependencyStatus.Timeout, health.Status);
        Assert.NotNull(health.Error);
    }

    [Fact]
    public async Task CheckNodeDirectoryPropagatesCallerCancellation()
    {
        var probe = ULinkRpcClusterDependencyProbe.ForNodeDirectory(
            new HangingNodeDirectory(),
            "local",
            "node-a",
            TimeSpan.FromSeconds(1));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        var exception = await Record.ExceptionAsync(async () =>
            await probe.CheckAsync(canceled.Token));
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
    }

    [Fact]
    public async Task CheckNodeDirectoryReturnsUnhealthyWhenDirectoryThrows()
    {
        var probe = ULinkRpcClusterDependencyProbe.ForNodeDirectory(
            new ThrowingNodeDirectory(new InvalidOperationException("node directory failed")),
            "local",
            "node-a",
            TimeSpan.FromSeconds(1));

        var health = await probe.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal("node-directory", health.Name);
        Assert.Equal(ULinkRpcClusterDependencyStatus.Unhealthy, health.Status);
        Assert.Contains("node directory failed", health.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckRouteDirectoryReturnsHealthyWhenResolveCompletes()
    {
        var probe = new ULinkRpcClusterDependencyProbe(
            new StaticClientFactory(new ResolvingClient()),
            TimeSpan.FromSeconds(1));

        var health = await probe.CheckRouteDirectoryAsync(
            NewDirectoryLocation(),
            TestContext.Current.CancellationToken);

        Assert.Equal("route-directory", health.Name);
        Assert.Equal(ULinkRpcClusterDependencyStatus.Healthy, health.Status);
        Assert.Null(health.Error);
    }

    [Fact]
    public async Task CheckRouteDirectoryReturnsTimeoutWithoutHanging()
    {
        var probe = new ULinkRpcClusterDependencyProbe(
            new StaticClientFactory(new HangingClient()),
            TimeSpan.FromMilliseconds(1));

        var health = await probe.CheckRouteDirectoryAsync(
            NewDirectoryLocation(),
            TestContext.Current.CancellationToken);

        Assert.Equal(ULinkRpcClusterDependencyStatus.Timeout, health.Status);
        Assert.NotNull(health.Error);
    }

    [Fact]
    public async Task CheckRouteDirectoryPropagatesCallerCancellation()
    {
        var probe = new ULinkRpcClusterDependencyProbe(
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
        var probe = new ULinkRpcClusterDependencyProbe(
            new ThrowingClientFactory(new InvalidOperationException("connect failed")),
            TimeSpan.FromSeconds(1));

        var health = await probe.CheckRouteDirectoryAsync(
            NewDirectoryLocation(),
            TestContext.Current.CancellationToken);

        Assert.Equal(ULinkRpcClusterDependencyStatus.Unhealthy, health.Status);
        Assert.Contains("connect failed", health.Error, StringComparison.Ordinal);
    }

    private static RouteLocation NewDirectoryLocation()
    {
        return new RouteLocation(
            "directory",
            "directory",
            new NodeEndpoint("tcp://127.0.0.1:21001"),
            DateTimeOffset.UtcNow.AddMinutes(1),
            nodeEpoch: 1,
            generation: 1);
    }

    private sealed class StaticClientFactory : IULinkRpcClusterClientFactory
    {
        private readonly IRpcClient _client;

        public StaticClientFactory(IRpcClient client)
        {
            _client = client;
        }

        public ValueTask<IRpcClient> GetClientAsync(
            RouteLocation target,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_client);
        }
    }

    private sealed class ThrowingClientFactory : IULinkRpcClusterClientFactory
    {
        private readonly Exception _exception;

        public ThrowingClientFactory(Exception exception)
        {
            _exception = exception;
        }

        public ValueTask<IRpcClient> GetClientAsync(
            RouteLocation target,
            CancellationToken cancellationToken = default)
        {
            throw _exception;
        }
    }

    private sealed class ResolvingClient : IRpcClient
    {
        public ValueTask<TResult> CallAsync<TArg, TResult>(
            RpcMethod<TArg, TResult> method,
            TArg? arg,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            object reply = new ULinkRpcRouteResolveReply();
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

    private sealed class HangingNodeDirectory : INodeDirectory
    {
        public ValueTask<NodeRegistrationResult> RegisterAsync(
            NodeRegistration registration,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<NodeHeartbeatStatus> HeartbeatAsync(
            string clusterName,
            NodeId node,
            long nodeEpoch,
            DateTimeOffset leaseExpiresAt,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<NodeStateUpdateStatus> UpdateStateAsync(
            string clusterName,
            NodeId node,
            long nodeEpoch,
            NodeState state,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public async ValueTask<NodeRecord?> ResolveAsync(
            string clusterName,
            NodeId node,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }

        public ValueTask<IReadOnlyList<NodeRecord>> QueryAsync(
            NodeDirectoryQuery query,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<int> ExpireAsync(
            string clusterName,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class ThrowingNodeDirectory : INodeDirectory
    {
        private readonly Exception _exception;

        public ThrowingNodeDirectory(Exception exception)
        {
            _exception = exception;
        }

        public ValueTask<NodeRegistrationResult> RegisterAsync(
            NodeRegistration registration,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<NodeHeartbeatStatus> HeartbeatAsync(
            string clusterName,
            NodeId node,
            long nodeEpoch,
            DateTimeOffset leaseExpiresAt,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<NodeStateUpdateStatus> UpdateStateAsync(
            string clusterName,
            NodeId node,
            long nodeEpoch,
            NodeState state,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<NodeRecord?> ResolveAsync(
            string clusterName,
            NodeId node,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            throw _exception;
        }

        public ValueTask<IReadOnlyList<NodeRecord>> QueryAsync(
            NodeDirectoryQuery query,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<int> ExpireAsync(
            string clusterName,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
