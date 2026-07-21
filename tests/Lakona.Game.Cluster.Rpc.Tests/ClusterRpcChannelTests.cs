using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Rpc.Core;
using Lakona.Rpc.Transport.Loopback;
using Xunit;

namespace Lakona.Game.Cluster.Rpc.Tests;

public sealed class ClusterRpcChannelTests
{
    [Fact]
    public async Task ConnectAsync_rejects_a_peer_with_a_different_serializer_protocol_before_rpc_starts()
    {
        LoopbackTransport.CreatePair(out var clientTransport, out var serverTransport);
        var clientChannel = new ClusterRpcChannel(
            new SingleConnectionClusterTransport(clientTransport),
            new StubClusterSerializer("lakona.cluster.memorypack.v1"));
        var serverChannel = new ClusterRpcChannel(
            new SingleConnectionClusterTransport(serverTransport),
            new StubClusterSerializer("lakona.cluster.json.v1"));
        var endpoint = new ClusterEndpoint("loopback", "local", 21001);
        await using var acceptor = await serverChannel.ListenAsync(
            endpoint,
            TestContext.Current.CancellationToken);
        using var stopAccepting = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var accepted = await acceptor.AcceptAsync(stopAccepting.Token);
        var serverTask = accepted.Transport.ConnectAsync(stopAccepting.Token).AsTask();
        var target = new RouteLocation(
            "cluster",
            "node-b",
            new NodeEndpoint("loopback://local:21001"),
            DateTimeOffset.UtcNow.AddMinutes(1));

        var exception = await Assert.ThrowsAsync<ClusterRpcProtocolMismatchException>(async () =>
            await clientChannel.ConnectAsync(target, TestContext.Current.CancellationToken));

        var serverException = await Assert.ThrowsAsync<ClusterRpcProtocolMismatchException>(() => serverTask);
        Assert.Equal("lakona.cluster.memorypack.v1", exception.LocalProtocolId);
        Assert.Equal("lakona.cluster.json.v1", exception.RemoteProtocolId);
        Assert.Equal("lakona.cluster.json.v1", serverException.LocalProtocolId);
        Assert.Equal("lakona.cluster.memorypack.v1", serverException.RemoteProtocolId);
    }

    private sealed class SingleConnectionClusterTransport(ITransport transport) : IClusterRpcTransport
    {
        public string Scheme => "loopback";

        public async ValueTask<ITransport> ConnectAsync(
            RouteLocation target,
            ClusterEndpoint endpoint,
            CancellationToken cancellationToken = default)
        {
            await transport.ConnectAsync(cancellationToken);
            return transport;
        }

        public ValueTask<IRpcConnectionAcceptor> ListenAsync(
            ClusterEndpoint endpoint,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IRpcConnectionAcceptor>(new SingleConnectionAcceptor(transport));
    }

    private sealed class SingleConnectionAcceptor(ITransport transport) : IRpcConnectionAcceptor
    {
        private int _accepted;

        public string ListenAddress => "loopback://local:21001";

        public async ValueTask<RpcAcceptedConnection> AcceptAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Exchange(ref _accepted, 1) == 0)
            {
                return new RpcAcceptedConnection(transport, "loopback");
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException();
        }

        public ValueTask DisposeAsync() => default;
    }

    private sealed class StubClusterSerializer(string protocolId) : IClusterRpcSerializer
    {
        public string ProtocolId { get; } = protocolId;

        public IRpcSerializer CreateSerializer() => new NoopSerializer();
    }

    private sealed class NoopSerializer : IRpcSerializer
    {
        public TransportFrame SerializeFrame<T>(T value) => throw new NotSupportedException();

        public T Deserialize<T>(ReadOnlySpan<byte> payload) => throw new NotSupportedException();

        public T Deserialize<T>(ReadOnlyMemory<byte> payload) => throw new NotSupportedException();
    }
}
