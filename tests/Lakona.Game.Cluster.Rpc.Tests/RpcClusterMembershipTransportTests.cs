using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Cluster.Rpc.Membership;
using Lakona.Rpc.Core;
using Xunit;

namespace Lakona.Game.Cluster.Rpc.Tests;

public sealed class RpcClusterMembershipTransportTests
{
    [Fact]
    public async Task RequestAsyncTimesOutWhenThePeerDoesNotReply()
    {
        var client = new PendingRpcClient();
        var transport = new RpcClusterMembershipTransport(
            new StubClientFactory(client),
            TimeSpan.FromMilliseconds(10));

        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await transport.RequestAsync(
                new NodeEndpoint("tcp://127.0.0.1:21002"),
                new ClusterMembershipTransportFrame(new byte[] { 1 }),
                TestContext.Current.CancellationToken));

        Assert.True(client.CancellationObserved);
    }

    [Fact]
    public async Task RequestAsyncPreservesCallerCancellation()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var client = new PendingRpcClient();
        var transport = new RpcClusterMembershipTransport(
            new StubClientFactory(client),
            TimeSpan.FromMinutes(1));

        var request = transport.RequestAsync(
            new NodeEndpoint("tcp://127.0.0.1:21002"),
            new ClusterMembershipTransportFrame(new byte[] { 1 }),
            cancellation.Token).AsTask();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
    }

    private sealed class StubClientFactory(IRpcClient client) : IClusterClientFactory
    {
        public ValueTask<IRpcClient> GetClientAsync(
            RouteLocation target,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<IRpcClient>(client);
        }
    }

    private sealed class PendingRpcClient : IRpcClient
    {
        public bool CancellationObserved { get; private set; }

        public async ValueTask<TResult> CallAsync<TArg, TResult>(
            RpcMethod<TArg, TResult> method,
            TArg? arg,
            CancellationToken ct = default)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                CancellationObserved = true;
                throw;
            }

            throw new InvalidOperationException("The pending RPC unexpectedly completed.");
        }

        public void RegisterNotificationHandler<TArg>(
            RpcNotificationMethod<TArg> method,
            Func<TArg, ValueTask> handler)
        {
            throw new NotSupportedException();
        }
    }
}
