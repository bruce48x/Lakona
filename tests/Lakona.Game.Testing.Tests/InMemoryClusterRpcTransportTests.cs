using Lakona.Game.Testing;
using Xunit;

namespace Lakona.Game.Testing.Tests;

public sealed class InMemoryClusterRpcTransportTests
{
    [Fact]
    public async Task AcceptedConnectionIsConnectedAndPartitionControlsExistingLink()
    {
        var network = new LakonaTestNetwork();
        await using var hub = new InMemoryClusterTransportHub(network);
        var endpoint = $"{InMemoryClusterTransportHub.Scheme}://127.0.0.1:30000";
        await using var acceptor = await hub.ListenAsync(
            "data-1",
            endpoint,
            TestContext.Current.CancellationToken);
        await using var client = await hub.ConnectAsync(
            "battle-1",
            endpoint,
            TestContext.Current.CancellationToken);
        var accepted = await acceptor.AcceptAsync(TestContext.Current.CancellationToken);
        await using var server = accepted.Transport;

        Assert.True(server.IsConnected);
        await client.ConnectAsync(TestContext.Current.CancellationToken);
        await client.SendFrameAsync(new byte[] { 1 }, TestContext.Current.CancellationToken);
        using (var received = await server.ReceiveFrameAsync(TestContext.Current.CancellationToken))
        {
            Assert.Equal(1, received.Memory.Span[0]);
        }

        network.Partition("battle-1", "data-1");
        await Assert.ThrowsAsync<IOException>(() =>
            client.SendFrameAsync(new byte[] { 2 }, TestContext.Current.CancellationToken).AsTask());

        network.Heal("battle-1", "data-1");
        await client.SendFrameAsync(new byte[] { 3 }, TestContext.Current.CancellationToken);
        using var healed = await server.ReceiveFrameAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, healed.Memory.Span[0]);
    }

    [Fact]
    public void NodeCannotBePartitionedFromItself()
    {
        var network = new LakonaTestNetwork();

        Assert.Throws<ArgumentException>(() => network.Partition("data-1", "data-1"));
    }

    [Fact]
    public async Task OneWayBlockLeavesReverseTrafficAvailable()
    {
        var network = new LakonaTestNetwork();
        await using var hub = new InMemoryClusterTransportHub(network);
        var endpoint = $"{InMemoryClusterTransportHub.Scheme}://127.0.0.1:30001";
        await using var acceptor = await hub.ListenAsync(
            "data-1",
            endpoint,
            TestContext.Current.CancellationToken);
        await using var client = await hub.ConnectAsync(
            "battle-1",
            endpoint,
            TestContext.Current.CancellationToken);
        var accepted = await acceptor.AcceptAsync(TestContext.Current.CancellationToken);
        await using var server = accepted.Transport;
        await client.ConnectAsync(TestContext.Current.CancellationToken);

        network.BlockOneWay("battle-1", "data-1");

        await Assert.ThrowsAsync<IOException>(() =>
            client.SendFrameAsync(new byte[] { 1 }, TestContext.Current.CancellationToken).AsTask());
        await server.SendFrameAsync(new byte[] { 2 }, TestContext.Current.CancellationToken);
        using (var reverse = await client.ReceiveFrameAsync(TestContext.Current.CancellationToken))
        {
            Assert.Equal(2, reverse.Memory.Span[0]);
        }

        network.HealOneWay("battle-1", "data-1");
        await client.SendFrameAsync(new byte[] { 3 }, TestContext.Current.CancellationToken);
        using var healed = await server.ReceiveFrameAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, healed.Memory.Span[0]);
    }
}
